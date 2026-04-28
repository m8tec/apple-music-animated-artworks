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
    MetadataResolutionCache metadataResolutionCache,
    KeyedLocker locker)
{
    private static readonly TimeSpan NegativeSearchCacheTtl = TimeSpan.FromDays(30);

    [GeneratedRegex(@"album/(?:[^/]+/)?(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex AlbumIdRegex();

    [GeneratedRegex(@"playlist/(?:[^/]+/)?(pl\.[a-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PlaylistIdRegex();

    private static bool NeedsTallArtworkRefresh(ArtworkCacheEntry entry)
    {
        bool hasSquareArtwork = !string.IsNullOrWhiteSpace(entry.M3u8Url) && entry.M3u8Url != "NONE";
        bool missingTallArtwork = string.IsNullOrWhiteSpace(entry.M3u8UrlTall);
        bool updatedBeforeTallImplement = entry.LastFetched.Date < DateTimeOffset.FromUnixTimeSeconds(1777391211).UtcDateTime;

        return hasSquareArtwork && missingTallArtwork && updatedBeforeTallImplement;
    }

    private string NormalizeUrl(string url)
    {
        var albumMatch = AlbumIdRegex().Match(url);
        if (albumMatch.Success)
        {
            var id = albumMatch.Groups[1].Value;
            return $"https://music.apple.com/album/{id}";
        }

        var playlistMatch = PlaylistIdRegex().Match(url);
        if (playlistMatch.Success)
        {
            var id = playlistMatch.Groups[1].Value;
            return $"https://music.apple.com/playlist/{id}";
        }

        return url.ToLowerInvariant().Trim();
    }

    public async Task<(ArtworkCacheEntry? Entry, bool IsCached)> GetArtworkByUrlAsync(string appleMusicUrl,
        CancellationToken ct = default)
    {
        string normalizedUrl = NormalizeUrl(appleMusicUrl);

        ArtworkCacheEntry? cachedEntry = cache.GetByUrl(normalizedUrl);
        if (cachedEntry != null && !NeedsTallArtworkRefresh(cachedEntry))
        {
            Log.Information("URL cache hit: {AppleMusicUrl}", normalizedUrl);
            return (cachedEntry, true);
        }

        Log.Information(
            cachedEntry != null
                ? "Cache entry is missing tall artwork: {AppleMusicUrl}"
                : "URL cache miss: {AppleMusicUrl}", normalizedUrl);

        SemaphoreSlim semaphore = locker.GetLock(normalizedUrl);
        await semaphore.WaitAsync(ct);

        try
        {
            cachedEntry = cache.GetByUrl(normalizedUrl);
            if (cachedEntry != null && !NeedsTallArtworkRefresh(cachedEntry))
            {
                Log.Information("URL cache hit after lock: {AppleMusicUrl}", normalizedUrl);
                return (cachedEntry, true);
            }

            AppleMusicPageParseResult parseResult = await appleMusicClient.ParseAppleMusicPageAsync(appleMusicUrl, ct);

            if (parseResult.Status == AppleMusicPageParseStatus.RateLimited)
            {
                Log.Information("Album page request was rate limited for URL: {AppleMusicUrl}.", normalizedUrl);

                // Return cached entry if available, even if it may be stale.
                return (cachedEntry, cachedEntry != null);
            }

            if (parseResult.Status == AppleMusicPageParseStatus.Error)
            {
                Log.Information("Album page request failed for URL: {AppleMusicUrl}.", normalizedUrl);
                return (null, false);
            }

            string? m3U8Url = parseResult.UrlSquare;
            string? m3U8UrlTall = parseResult.UrlTall;
            string artist = parseResult.Artist;
            string album = parseResult.Album;
            int downloadCount = cachedEntry?.DownloadCount ?? 0;
            int searchCount = cachedEntry?.SearchCount ?? 0;

            ArtworkCacheEntry newEntry = new(
                AppleMusicUrl: normalizedUrl,
                Artist: artist,
                Album: album,
                M3u8Url: m3U8Url ?? "NONE",
                M3u8UrlTall: m3U8UrlTall ?? "NONE",
                LastFetched: DateTime.UtcNow,
                DownloadCount: downloadCount,
                SearchCount: searchCount
            );

            await cache.SaveEntryAsync(newEntry);
            Log.Information("Saved URL cache entry: {AppleMusicUrl}, Artist: {Artist}, Album: {Album}, HasAnimatedArtwork: {HasAnimatedArtwork}",
                normalizedUrl,
                artist,
                album,
                m3U8Url != null || m3U8UrlTall != null);

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
        MetadataResolutionLookup lookup = metadataResolutionCache.GetLookup(artist, album, NegativeSearchCacheTtl);

        if (lookup.Status == MetadataResolutionStatus.NoMatch)
        {
            Log.Information("Metadata resolution no-match cache hit: Artist={Artist}, Album={Album}.", artist, album);
            return (null, false);
        }

        if (lookup.Status == MetadataResolutionStatus.Resolved && !string.IsNullOrWhiteSpace(lookup.ResolvedAppleMusicUrl))
        {
            Log.Information("Metadata resolution cache hit: Artist={Artist}, Album={Album} -> {AppleMusicUrl}", artist, album, lookup.ResolvedAppleMusicUrl);

            (ArtworkCacheEntry? resolvedEntry, bool resolvedIsCached) = await GetArtworkByUrlAsync(lookup.ResolvedAppleMusicUrl, ct);
            if (resolvedEntry != null)
            {
                return (resolvedEntry, resolvedIsCached);
            }

            Log.Information("Metadata resolution cache entry is stale. Removing alias for Artist={Artist}, Album={Album}", artist, album);
            await metadataResolutionCache.RemoveResolvedUrlAsync(artist, album);
        }

        ArtworkCacheEntry? cachedEntry = cache.GetByArtistAndAlbum(artist, album);
        if (cachedEntry != null)
        {
            if (!NeedsTallArtworkRefresh(cachedEntry))
            {
                Log.Information("Cache hit (by metadata): Artist={Artist}, Album={Album}.", artist, album);
                return (cachedEntry, true);
            }

            Log.Information("Cache entry is missing tall artwork (by metadata): Artist={Artist}, Album={Album}.", artist, album);
        }
        else
        {
            Log.Information("Cache miss (by metadata): Artist={Artist}, Album={Album}", artist, album);
        }

        Log.Information("Triggering web search: Artist={Artist}, Album={Album}, Title={Title}", artist, album, title ?? "");

        AppleMusicWebSearchResult webSearchResult = await appleMusicClient.GetAppleMusicUrlViaWebSearchAsync(artist, album, title, ct);

        if (webSearchResult.Status == AppleMusicWebSearchStatus.NoMatch)
        {
            await metadataResolutionCache.SaveNoMatchAsync(artist, album);
            Log.Information("Search returned no match for Artist={Artist}, Album={Album}", artist, album);
            return (null, false);
        }

        if (webSearchResult.Status == AppleMusicWebSearchStatus.RateLimited)
        {
            Log.Information("Search was rate limited for Artist={Artist}, Album={Album}", artist, album);
            // Return cached entry if available, even if it may be stale.
            return (cachedEntry, cachedEntry != null);
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
        await metadataResolutionCache.SaveResolvedUrlAsync(artist, album, webSearchResult.Url);

        return await GetArtworkByUrlAsync(webSearchResult.Url, ct);
    }
}