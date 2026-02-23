namespace AnimatedArtworks.Application;

using AnimatedArtworks.Domain;
using AnimatedArtworks.Infrastructure; // Wichtig für den JsonCacheService

public class ArtworkService(
    IAppleMusicClient appleMusicClient,
    JsonCacheService cache,
    KeyedLocker locker)
{
    public async Task<ArtworkCacheEntry> GetArtworkByUrlAsync(string appleMusicUrl, CancellationToken ct = default)
    {
        var cachedEntry = cache.GetByUrl(appleMusicUrl);
        if (cachedEntry != null) return cachedEntry;

        var semaphore = locker.GetLock(appleMusicUrl);
        await semaphore.WaitAsync(ct);

        try
        {
            cachedEntry = cache.GetByUrl(appleMusicUrl);
            if (cachedEntry != null) return cachedEntry;

            var (m3u8Url, artist, album) = await appleMusicClient.ParseAppleMusicPageAsync(appleMusicUrl, ct);

            var newEntry = new ArtworkCacheEntry(
                AppleMusicUrl: appleMusicUrl,
                Artist: artist,
                Album: album,
                M3u8Url: m3u8Url ?? "NONE",
                LastFetched: DateTime.UtcNow
            );

            await cache.SaveEntryAsync(newEntry);

            return newEntry;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<ArtworkCacheEntry?> GetArtworkByDetailsAsync(string artist, string album, CancellationToken ct = default)
    {
        var cachedEntry = cache.GetByArtistAndAlbum(artist, album);
        if (cachedEntry != null) return cachedEntry;

        var appleMusicUrl = await appleMusicClient.GetAppleMusicUrlAsync(artist, album, ct);
        
        if (string.IsNullOrEmpty(appleMusicUrl)) 
        {
            return null;
        }
        
        return await GetArtworkByUrlAsync(appleMusicUrl, ct);
    }
}