using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AnimatedArtworks.Infrastructure;

namespace AnimatedArtworks.Application;

public partial class ArtworkService(
    IAppleMusicClient appleMusicClient,
    JsonCacheService cache,
    KeyedLocker locker)
{
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

    public async Task<ArtworkCacheEntry?> GetArtworkByUrlAsync(string appleMusicUrl, CancellationToken ct = default)
    {
        string normalizedUrl = NormalizeUrl(appleMusicUrl);

        ArtworkCacheEntry? cachedEntry = cache.GetByUrl(normalizedUrl);
        if (cachedEntry != null) return cachedEntry;

        SemaphoreSlim semaphore = locker.GetLock(normalizedUrl);
        await semaphore.WaitAsync(ct);

        try
        {
            cachedEntry = cache.GetByUrl(normalizedUrl);
            if (cachedEntry != null) return cachedEntry;

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

            return newEntry;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task<ArtworkCacheEntry?> GetArtworkByDetailsAsync(string artist, string album, string? title = null,
        CancellationToken ct = default)
    {
        ArtworkCacheEntry? cachedEntry = cache.GetByArtistAndAlbum(artist, album);
        if (cachedEntry != null) return cachedEntry;

        string? appleMusicUrl = await appleMusicClient.GetAppleMusicUrlAsync(artist, album, title, ct);
        
        if (string.IsNullOrEmpty(appleMusicUrl)) 
        {
            return null;
        }
        
        return await GetArtworkByUrlAsync(appleMusicUrl, ct);
    }
}