using AnimatedArtworks.Application;
using AnimatedArtworks.Infrastructure;
using Microsoft.AspNetCore.Mvc;
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

    builder.Services.AddSingleton<JsonCacheService>();
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

    app.MapGet("/api/v1/artwork", async (
        [FromQuery] string artist, 
        [FromQuery] string album, 
        [FromServices] ArtworkService service,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct) =>
    {
        logger.LogInformation("Incoming Request: Metadata Search -> Artist: {Artist}, Album: {Album}", artist, album);

        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(album))
            return Results.BadRequest("Artist and Album must be provided.");

        var result = await service.GetArtworkByDetailsAsync(artist, album, ct);

        if (result != null && result.M3u8Url != "NONE")
        {
            return Results.Ok(new { url = result.M3u8Url, artist = result.Artist, album = result.Album });
        }
    
        return Results.NotFound(new { message = "No animated artwork found." });
    });

    app.MapGet("/api/v1/artwork/by-url", async (
        [FromQuery] string url, 
        [FromServices] ArtworkService service,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct) =>
    {
        logger.LogInformation("Incoming Request: URL Search -> {AppleMusicUrl}", url);
        
        if (string.IsNullOrWhiteSpace(url) || !url.Contains("music.apple.com"))
            return Results.BadRequest("A valid Apple Music URL must be provided.");

        var result = await service.GetArtworkByUrlAsync(url, ct);

        if (result != null && result.M3u8Url != "NONE")
        {
            return Results.Ok(new { url = result.M3u8Url, artist = result.Artist, album = result.Album });
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
