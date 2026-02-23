namespace AnimatedArtworks.Application;

public class ArtworkService(
    IAppleMusicClient appleMusicClient,
    ICacheService cache,
    KeyedLocker locker)
{
    private string GetCacheKey(string artist, string album) => 
        $"{artist.ToLowerInvariant().Trim()}_{album.ToLowerInvariant().Trim()}"
            .Replace(" ", "_").Replace("-", "_").Replace("/", "_");

    public async Task<string?> GetM3u8UrlAsync(ArtworkRequest request, CancellationToken ct = default)
    {
        var fileKey = GetCacheKey(request.Artist, request.Album);
        var cacheKey = $"m3u8_{fileKey}";

        var cachedUrl = await cache.GetAsync<string>(cacheKey, ct);
        if (cachedUrl != null) return cachedUrl == "NONE" ? null : cachedUrl;

        var semaphore = locker.GetLock(cacheKey);
        await semaphore.WaitAsync(ct);

        try
        {
            cachedUrl = await cache.GetAsync<string>(cacheKey, ct);
            if (cachedUrl != null) return cachedUrl == "NONE" ? null : cachedUrl;

            var url = await appleMusicClient.FetchAnimatedArtworkUrlAsync(request.Artist, request.Album, ct);
            
            await cache.SetAsync(cacheKey, url ?? "NONE", Timeout.InfiniteTimeSpan, ct);

            return url;
        }
        finally
        {
            semaphore.Release();
        }
    }
}