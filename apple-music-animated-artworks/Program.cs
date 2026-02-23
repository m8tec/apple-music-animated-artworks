using AnimatedArtworks.Application;
using AnimatedArtworks.Domain;
using AnimatedArtworks.Infrastructure;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ICacheService, FileCacheService>();
builder.Services.AddScoped<ArtworkService>();
builder.Services.AddHttpClient<IAppleMusicClient, AppleMusicClient>(client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15");
    client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
});
builder.Services.AddSingleton<KeyedLocker>();
builder.Services.AddSingleton<IHistoryService, MemoryHistoryService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/v1/artwork", async (
    [FromQuery] string artist, 
    [FromQuery] string album, 
    [FromServices] ArtworkService service, 
    [FromServices] IHistoryService history, 
    CancellationToken ct) =>
{
    var request = new ArtworkRequest(artist, album);
    var url = await service.GetM3u8UrlAsync(request, ct);

    if (url != null)
    {
        history.AddSuccessfulSearch(artist, album, url);
        return Results.Ok(new { url });
    }
    
    return Results.NotFound(new { message = "No animated artwork found." });
});

app.MapGet("/api/v1/artwork/history", ([FromServices] IHistoryService history) =>
{
    return Results.Ok(history.GetRecentSearches());
});

app.Run();