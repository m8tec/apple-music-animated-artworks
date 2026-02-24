using System;
using System.Linq;
using System.Threading;
using AnimatedArtworks.Application;
using AnimatedArtworks.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        theme: Serilog.Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code
    )
    .CreateLogger();

try 
{
    Log.Information("Starting Artwork Finder Web API...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog();

    var cachePath = builder.Configuration["CACHE_FILE_PATH"] ?? "cache_database.json";
    builder.Services.AddSingleton(new JsonCacheService(cachePath));
    
    builder.Services.AddSingleton<SystemStatusService>();
    
    builder.Services.AddSingleton<KeyedLocker>();

    builder.Services.AddHttpClient<IAppleMusicClient, AppleMusicClient>(client =>
    {
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
    });

    builder.Services.AddScoped<ArtworkService>();

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    app.UseDefaultFiles();
    app.UseStaticFiles();
    
    app.MapGet("/api/v1/status", ([FromServices] SystemStatusService statusService) =>
    {
        if (statusService.IsRateLimited && (DateTime.UtcNow - statusService.LastErrorTime).TotalMinutes > 15)
        {
            statusService.IsRateLimited = false;
        }

        if (statusService.IsRateLimited)
        {
            return Results.Ok(new { status = "degraded", message = "Apple Music Rate Limit. May be unstable." });
        }
        
        return Results.Ok(new { status = "operational", message = "System Operational" });
    });

    app.MapGet("/api/v1/artwork/search", async (
        [FromQuery] string artist, 
        [FromQuery] string album, 
        [FromQuery] string? title,
        [FromServices] ArtworkService service,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct) =>
    {
        logger.LogInformation("Incoming Request: Metadata Search -> Artist: {Artist}, Album: {Album}, Title: {Title}", 
            artist, album, title ?? "N/A");
        
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(album))
            return Results.BadRequest("Artist and Album must be provided.");
        
        (ArtworkCacheEntry? entry, bool isCached) = await service.GetArtworkByDetailsAsync(artist, album, title, ct);

        if (entry != null && entry.M3u8Url != "NONE")
        {
            return Results.Ok(new { url = entry.M3u8Url, artist = entry.Artist, album = entry.Album, isCached });
        }

        return Results.NotFound(new { message = "No animated artwork found." });
    });

    app.MapGet("/api/v1/artwork/url", async (
        [FromQuery] string url, 
        [FromServices] ArtworkService service,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct) =>
    {
        logger.LogInformation("Incoming Request: URL Search -> {AppleMusicUrl}", url);
        
        if (string.IsNullOrWhiteSpace(url) || !url.Contains("music.apple.com"))
            return Results.BadRequest("A valid Apple Music URL must be provided.");

        var (entry, isCached) = await service.GetArtworkByUrlAsync(url, ct);

        if (entry != null && entry.M3u8Url != "NONE")
        {
            return Results.Ok(new { url = entry.M3u8Url, artist = entry.Artist, album = entry.Album, isCached });
        }
    
        return Results.NotFound(new { message = "No animated artwork found." });
    });

    app.MapGet("/api/v1/artwork/history", ([FromServices] JsonCacheService cache) =>
    {
        var recent = cache.GetRecentSearches(10).Select(x => new 
        {
            artist = x.Artist,
            album = x.Album,
            url = x.M3u8Url,
            fetchedAt = x.LastFetched
        });
    
        return Results.Ok(recent);
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
