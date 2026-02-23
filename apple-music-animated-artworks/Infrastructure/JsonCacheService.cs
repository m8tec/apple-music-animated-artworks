namespace AnimatedArtworks.Infrastructure;

using System.Collections.Concurrent;
using System.Text.Json;
using AnimatedArtworks.Domain;

public class JsonCacheService
{
    private readonly string _filePath = Path.Combine(Directory.GetCurrentDirectory(), "cache_database.json");
    private readonly ConcurrentDictionary<string, ArtworkCacheEntry> _cache = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public JsonCacheService()
    {
        if (File.Exists(_filePath))
        {
            var json = File.ReadAllText(_filePath);
            var entries = JsonSerializer.Deserialize<List<ArtworkCacheEntry>>(json) ?? new();
            foreach (var entry in entries)
            {
                _cache[entry.AppleMusicUrl] = entry;
            }
        }
    }

    public ArtworkCacheEntry? GetByUrl(string appleMusicUrl)
    {
        _cache.TryGetValue(appleMusicUrl, out var entry);
        return entry;
    }

    public ArtworkCacheEntry? GetByArtistAndAlbum(string artist, string album)
    {
        return _cache.Values.FirstOrDefault(x => 
            x.Artist.Equals(artist, StringComparison.OrdinalIgnoreCase) && 
            x.Album.Equals(album, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SaveEntryAsync(ArtworkCacheEntry newEntry)
    {
        _cache[newEntry.AppleMusicUrl] = newEntry;

        await _fileLock.WaitAsync();
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_cache.Values, options);
            await File.WriteAllTextAsync(_filePath, json);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public IEnumerable<ArtworkCacheEntry> GetRecentSearches(int limit = 10)
    {
        return _cache.Values
            .Where(x => x.M3u8Url != null && x.M3u8Url != "NONE")
            .OrderByDescending(x => x.LastFetched)
            .Take(limit);
    }
}