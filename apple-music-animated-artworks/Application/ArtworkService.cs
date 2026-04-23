using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AnimatedArtworks.Infrastructure;
using Serilog;

namespace AnimatedArtworks.Application;

public partial class ArtworkService(
    IAppleMusicClient appleMusicClient,
    JsonCacheService cache,
    KeyedLocker locker)
{
    private static readonly TimeSpan NegativeSearchCacheTtl = TimeSpan.FromDays(30);

    [GeneratedRegex(@"album/.*/(\d+)|album/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex AlbumIdRegex();

    private string NormalizeUrl(string url)
    {
        var match = AlbumIdRegex().Match(url);
        if (match.Success)
        {
            var id = !string.IsNullOrEmpty(match.Groups[1].Value) 
                     ? match.Groups[1].Value 
                     : match.Groups[2].Value;
            
            return $"https://music.apple.com/album/{id}";
        }
        return url.ToLowerInvariant().Trim();
    }

    public async Task<(ArtworkCacheEntry? Entry, bool IsCached)> GetArtworkByUrlAsync(string appleMusicUrl,
        CancellationToken ct = default)
    {
        string normalizedUrl = NormalizeUrl(appleMusicUrl);

        ArtworkCacheEntry? cachedEntry = cache.GetByUrl(normalizedUrl);
        if (cachedEntry != null)
        {
            Log.Information("Cache hit (by URL): {AppleMusicUrl}", normalizedUrl);
            return (cachedEntry, true);
        }

        Log.Information("Cache miss (by URL): {AppleMusicUrl}", normalizedUrl);

        SemaphoreSlim semaphore = locker.GetLock(normalizedUrl);
        await semaphore.WaitAsync(ct);

        try
        {
            cachedEntry = cache.GetByUrl(normalizedUrl);
            if (cachedEntry != null)
            {
                Log.Information("Cache hit after lock (by URL): {AppleMusicUrl}", normalizedUrl);
                return (cachedEntry, true);
            }

            (string? m3u8Url, string artist, string album) =
                await appleMusicClient.ParseAppleMusicPageAsync(appleMusicUrl, ct);

            ArtworkCacheEntry newEntry = new(
                AppleMusicUrl: normalizedUrl,
                Artist: artist,
                Album: album,
                M3u8Url: m3u8Url ?? "NONE",
                LastFetched: DateTime.UtcNow
            );

            await cache.SaveEntryAsync(newEntry);
            Log.Information("Saved cache entry (by URL): {AppleMusicUrl}, Artist: {Artist}, Album: {Album}, HasAnimatedArtwork: {HasAnimatedArtwork}",
                normalizedUrl,
                artist,
                album,
                m3u8Url != null);

            return (newEntry, false);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<(ArtworkCacheEntry? Entry, bool IsCached)> GetArtworkByDetailsAsync(string artist, string album, string? title = null,
        CancellationToken ct = default)
    {
        ArtworkCacheEntry? cachedEntry = cache.GetByArtistAndAlbum(artist, album);
        if (cachedEntry != null)
        {
            if (!cache.IsNegativeSearchEntry(cachedEntry))
            {
                Log.Information("Cache hit (by metadata): Artist={Artist}, Album={Album} resolved to {CachedArtist} - {CachedAlbum}",
                    artist,
                    album,
                    cachedEntry.Artist,
                    cachedEntry.Album);
                return (cachedEntry, true);
            }

            if (DateTime.UtcNow - cachedEntry.LastFetched <= NegativeSearchCacheTtl)
            {
                Log.Information("Negative cache hit (by metadata): Artist={Artist}, Album={Album}. Skipping external search.", artist, album);
                return (cachedEntry, true);
            }

            Log.Information("Negative cache expired (by metadata): Artist={Artist}, Album={Album}. Performing external search.", artist, album);
        }
        else
        {
            Log.Information("Cache miss (by metadata): Artist={Artist}, Album={Album}", artist, album);
        }

        Log.Information("Triggering web search: Artist={Artist}, Album={Album}, Title={Title}", artist, album, title ?? "N/A");

        AppleMusicWebSearchResult webSearchResult = await appleMusicClient.GetAppleMusicUrlViaWebSearchAsync(artist, album, title, ct);

        if (webSearchResult.Status == AppleMusicWebSearchStatus.NoMatch)
        {
            await cache.SaveNegativeSearchResultAsync(artist, album);
            Log.Information("Web search returned no match. Negative cache saved for Artist={Artist}, Album={Album}", artist, album);
            return (null, false);
        }

        if (webSearchResult.Status == AppleMusicWebSearchStatus.RateLimited)
        {
            Log.Information("Search was rate limited for Artist={Artist}, Album={Album}", artist, album);
            return (null, false);
        }

        if (webSearchResult.Status == AppleMusicWebSearchStatus.Error)
        {
            Log.Information("Search failed unexpectedly for Artist={Artist}, Album={Album}", artist, album);
            return (null, false);
        }

        if (string.IsNullOrWhiteSpace(webSearchResult.Url))
        {
            Log.Warning("Search returned status Found without URL for Artist={Artist}, Album={Album}", artist, album);
            return (null, false);
        }

        Log.Information("Search resolved URL: {AppleMusicUrl}", webSearchResult.Url);

        return await GetArtworkByUrlAsync(webSearchResult.Url, ct);
    }
}