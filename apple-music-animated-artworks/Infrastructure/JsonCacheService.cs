using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AnimatedArtworks.Infrastructure;

using System.Collections.Concurrent;
using System.Text.Json;

public class JsonCacheService
{
    private string FilePath { get; set; }
    private readonly ConcurrentDictionary<string, ArtworkCacheEntry> _cache = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public JsonCacheService(string filePath)
    {
        FilePath = filePath;
        
        if (File.Exists(FilePath))
        {
            var json = File.ReadAllText(FilePath);
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
            await File.WriteAllTextAsync(FilePath, json);
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