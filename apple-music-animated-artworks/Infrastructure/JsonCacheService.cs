using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnimatedArtworks.Infrastructure;

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
    
    private static string NormalizeForCache(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        
        return new string(input.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
    
    public IEnumerable<ArtworkCacheEntry> GetAll() => _cache.Values;

    public ArtworkCacheEntry? GetByUrl(string appleMusicUrl)
    {
        _cache.TryGetValue(appleMusicUrl, out var entry);
        return entry;
    }

    public ArtworkCacheEntry? GetByArtistAndAlbum(string artist, string album)
    {
        string queryArtist = NormalizeForCache(artist);
        string queryAlbum = NormalizeForCache(album);

        if (string.IsNullOrEmpty(queryArtist) || string.IsNullOrEmpty(queryAlbum)) 
            return null;

        return _cache.Values
            .Where(x => 
            {
                string cachedArtist = NormalizeForCache(x.Artist);
                string cachedAlbum = NormalizeForCache(x.Album);

                bool artistMatch = cachedArtist.Contains(queryArtist) || queryArtist.Contains(cachedArtist);
                bool albumMatch = cachedAlbum.Contains(queryAlbum) || queryAlbum.Contains(cachedAlbum);

                return artistMatch && albumMatch;
            })
            // prefer existing m3u8-urls
            .OrderByDescending(x => x.M3u8Url != null && x.M3u8Url != "NONE")
            // prefer shorter album names, as they are more likely to be the original release instead of
            // a special edition (e.g. "A Cappella Super Deluxe Version")
            .ThenBy(x => x.Album.Length)
            .FirstOrDefault();
    }
    
    public async Task IncrementDownloadCountAsync(string m3u8Url)
    {
        await _fileLock.WaitAsync();
        try
        {
            var target = _cache.FirstOrDefault(x => x.Value.M3u8Url == m3u8Url);
            
            if (target.Key != null)
            {
                var entry = target.Value;
                
                var updatedEntry = entry with { DownloadCount = entry.DownloadCount + 1 };
                _cache[target.Key] = updatedEntry;
                
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true, 
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                };
                var json = JsonSerializer.Serialize(_cache.Values, options);
                await File.WriteAllTextAsync(FilePath, json);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }
    
    public async Task IncrementSearchCountAsync(string appleMusicUrl)
    {
        await _fileLock.WaitAsync();
        try
        {
            if (_cache.TryGetValue(appleMusicUrl, out var entry))
            {
                var updatedEntry = entry with { SearchCount = entry.SearchCount + 1 };
                _cache[appleMusicUrl] = updatedEntry;
                
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true, 
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                };
                var json = JsonSerializer.Serialize(_cache.Values, options);
                await File.WriteAllTextAsync(FilePath, json);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }
    
    public async Task SaveEntryAsync(ArtworkCacheEntry newEntry)
    {
        _cache[newEntry.AppleMusicUrl] = newEntry;

        await _fileLock.WaitAsync();
        try
        {
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
            };
            
            var json = JsonSerializer.Serialize(_cache.Values, options);
            await File.WriteAllTextAsync(FilePath, json);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public IEnumerable<ArtworkCacheEntry> GetRecentSearches(int limit = 12)
    {
        return _cache.Values
            .Where(x => x.M3u8Url != null && x.M3u8Url != "NONE")
            .OrderByDescending(x => x.LastFetched)
            .Take(limit);
    }
}