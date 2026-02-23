using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using AnimatedArtworks.Domain;
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
            // Wir extrahieren die ID (entweder aus Gruppe 1 oder 2)
            var id = !string.IsNullOrEmpty(match.Groups[1].Value) 
                     ? match.Groups[1].Value 
                     : match.Groups[2].Value;
            
            // Einheitliches Format ohne Country-Code und ohne Namen-Slug
            return $"https://music.apple.com/album/{id}";
        }
        return url.ToLowerInvariant().Trim();
    }

    public async Task<ArtworkCacheEntry> GetArtworkByUrlAsync(string appleMusicUrl, CancellationToken ct = default)
    {
        var normalizedUrl = NormalizeUrl(appleMusicUrl);

        var cachedEntry = cache.GetByUrl(normalizedUrl);
        if (cachedEntry != null) return cachedEntry;

        var semaphore = locker.GetLock(normalizedUrl);
        await semaphore.WaitAsync(ct);

        try
        {
            cachedEntry = cache.GetByUrl(normalizedUrl);
            if (cachedEntry != null) return cachedEntry;

            var (m3u8Url, artist, album) = await appleMusicClient.ParseAppleMusicPageAsync(appleMusicUrl, ct);

            var newEntry = new ArtworkCacheEntry(
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