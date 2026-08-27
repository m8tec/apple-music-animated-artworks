using System;
using System.Linq;
using System.Threading;
using System.Threading.RateLimiting;
using AnimatedArtworks.Application;
using AnimatedArtworks.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Hosting.Server;

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
    builder.Services.AddSingleton(_ => new JsonCacheService(cachePath));

    var metadataResolutionCachePath = builder.Configuration["METADATA_RESOLUTION_CACHE_FILE_PATH"] ?? "metadata_resolution_cache.json";
    builder.Services.AddSingleton(_ => new MetadataResolutionCache(metadataResolutionCachePath));
    builder.Services.AddHostedService<CacheInitializationHostedService>();

    builder.Services.AddSingleton<SystemStatusService>();
    
    builder.Services.AddSingleton<KeyedLocker>();

    builder.Services.AddHttpClient<IAppleMusicClient, AppleMusicClient>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(5);
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15");
        client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
    });

    builder.Services.AddScoped<ArtworkService>();

    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
    
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowAll", policy =>
        {
            policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (context, token) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                context.HttpContext.Response.Headers["Retry-After"] = retryAfter.TotalSeconds.ToString();
            }

            context.HttpContext.Response.ContentType = "text/plain";
            await context.HttpContext.Response.WriteAsync("Too many requests. Please slow down.", token);
        };

        options.AddPolicy("ApiRateLimit", httpContext =>
        {
            var remoteIp = httpContext.Connection.RemoteIpAddress;
            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            var clientIp = !string.IsNullOrWhiteSpace(forwardedFor)
                ? forwardedFor.Split(',')[0].Trim()
                : remoteIp?.ToString() ?? "global";

            return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromSeconds(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            });
        });
    });

    var app = builder.Build();

    app.UseForwardedHeaders();
    app.UseCors("AllowAll");
    app.UseRateLimiter();

    app.Lifetime.ApplicationStarted.Register(() =>
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
        if (addresses is { Count: > 0 })
        {
            Log.Information("Running on: {Addresses}", string.Join(", ", addresses));
        }
        else
        {
            Log.Information("Started, but no server addresses were available yet.");
        }
    });

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            var jsonCache = context.RequestServices.GetRequiredService<JsonCacheService>();
            var metadataCache = context.RequestServices.GetRequiredService<MetadataResolutionCache>();

            if (!jsonCache.IsInitialized || !metadataCache.IsInitialized)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync("Cache is still initializing. Please retry shortly.");
                return;
            }
        }

        await next();
    });
    
    app.MapGet("/api/v1/status", ([FromServices] SystemStatusService statusService, [FromServices] JsonCacheService cacheService) =>
    {
        var allEntries = cacheService.GetAll().ToList();
    
        int totalSearches = allEntries.Sum(e => e.SearchCount);
        int totalDownloads = allEntries.Sum(e => e.DownloadCount);
        int totalCacheEntries = allEntries.Count;
        int totalAnimatedEntries = allEntries.Count(e => (e.M3u8Url != null && e.M3u8Url != "NONE") || (e.M3u8UrlTall != null && e.M3u8UrlTall != "NONE"));

        if (statusService.IsRateLimited)
        {
            return Results.Ok(new
            {
                status = "degraded",
                message = "Apple Music Rate Limit. May be unstable.",
                totalSearches,
                totalDownloads,
                totalCacheEntries,
                totalAnimatedEntries
            });
        }
        
        return Results.Ok(new
            {
                status = "operational",
                message = "System Operational",
                totalSearches,
                totalDownloads,
                totalCacheEntries,
                totalAnimatedEntries
            });
    }).RequireRateLimiting("ApiRateLimit");

    app.MapGet("/api/v1/artwork/search", async (
        [FromQuery] string artist, 
        [FromQuery] string album, 
        [FromQuery] string? title,
        [FromServices] ArtworkService service,
        [FromServices] ILogger<Program> logger,
        [FromServices] JsonCacheService cacheService,
        CancellationToken ct) =>
    {
        logger.LogInformation("Incoming Request: Metadata Search -> Artist: {Artist}, Album: {Album}, Title: {Title}", 
            artist, album, title ?? "N/A");
        
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(album))
            return Results.BadRequest("Artist and Album must be provided.");
        
        (ArtworkCacheEntry? entry, bool isCached) = await service.GetArtworkByDetailsAsync(artist, album, title, ct);

        if (entry != null)
        {
            await cacheService.IncrementSearchCountAsync(entry);

            if ((entry.M3u8Url != null && entry.M3u8Url != "NONE") || (entry.M3u8UrlTall != null && entry.M3u8UrlTall != "NONE"))
            {
                return Results.Ok(new { 
                    url = entry.M3u8Url == "NONE" ? null : entry.M3u8Url, 
                    url_tall = entry.M3u8UrlTall == "NONE" ? null : entry.M3u8UrlTall, 
                    artist = entry.Artist, 
                    album = entry.Album, 
                    isCached 
                });
            }
        }

        return Results.NotFound(new { message = "No animated artwork found." });
    }).RequireRateLimiting("ApiRateLimit");

    app.MapGet("/api/v1/artwork/url", async (
        [FromQuery] string url, 
        [FromServices] ArtworkService service,
        [FromServices] ILogger<Program> logger,
        [FromServices] JsonCacheService cacheService,
        CancellationToken ct) =>
    {
        logger.LogInformation("Incoming Request: URL Search -> {AppleMusicUrl}", url);
        
        if (string.IsNullOrWhiteSpace(url) || !url.Contains("music.apple.com"))
            return Results.BadRequest("A valid Apple Music URL must be provided.");

        var (entry, isCached) = await service.GetArtworkByUrlAsync(url, ct);

        if (entry != null)
        {
            await cacheService.IncrementSearchCountAsync(entry);
            
            if ((entry.M3u8Url != null && entry.M3u8Url != "NONE") || (entry.M3u8UrlTall != null && entry.M3u8UrlTall != "NONE"))
            {
                return Results.Ok(new { 
                    url = entry.M3u8Url == "NONE" ? null : entry.M3u8Url, 
                    url_tall = entry.M3u8UrlTall == "NONE" ? null : entry.M3u8UrlTall, 
                    artist = entry.Artist, 
                    album = entry.Album, 
                    isCached 
                });
            }
        }
    
        return Results.NotFound(new { message = "No animated artwork found." });
    }).RequireRateLimiting("ApiRateLimit");
    
    app.MapPost("/api/v1/artwork/download", async (
        DownloadReportRequest req, 
        JsonCacheService cacheService) => 
    {
        if (string.IsNullOrWhiteSpace(req.M3U8Url))
            return Results.BadRequest();

        await cacheService.IncrementDownloadCountAsync(req.M3U8Url);
        
        return Results.Ok();
    }).RequireRateLimiting("ApiRateLimit");

    app.MapGet("/api/v1/artwork/history", ([FromServices] JsonCacheService cache) =>
    {
        var recent = cache.GetRecentSearches().Select(x => new 
        {
            artist = x.Artist,
            album = x.Album,
            url = x.M3u8Url == "NONE" ? null : x.M3u8Url,
            url_tall = x.M3u8UrlTall == "NONE" ? null : x.M3u8UrlTall,
            fetchedAt = x.LastFetched
        });
    
        return Results.Ok(recent);
    }).RequireRateLimiting("ApiRateLimit");

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

public record DownloadReportRequest(string M3U8Url);