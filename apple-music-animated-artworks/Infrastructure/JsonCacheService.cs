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
    private const string NegativeSearchPrefix = "search-miss:";
    private string FilePath { get; }
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

    private static bool IsNegativeSearchKey(string cacheKey)
    {
        return cacheKey.StartsWith(NegativeSearchPrefix, System.StringComparison.Ordinal);
    }

    private static string BuildNegativeSearchCacheKey(string artist, string album)
    {
        return $"{NegativeSearchPrefix}{NormalizeForCache(artist)}|{NormalizeForCache(album)}";
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

                if (IsNegativeSearchKey(x.AppleMusicUrl))
                {
                    // Negative search cache entries are intentionally exact to avoid false negatives.
                    return cachedArtist == queryArtist && cachedAlbum == queryAlbum;
                }

                bool artistMatch = cachedArtist.Contains(queryArtist) || queryArtist.Contains(cachedArtist);
                bool albumMatch = cachedAlbum.Contains(queryAlbum) || queryAlbum.Contains(cachedAlbum);

                return artistMatch && albumMatch;
            })
            // prefer existing m3u8-urls
            .OrderByDescending(x => (x.M3u8Url != null && x.M3u8Url != "NONE") || (x.M3u8UrlTall != null && x.M3u8UrlTall != "NONE"))
            // prefer regular cache entries over negative search markers when both match.
            .ThenBy(x => IsNegativeSearchKey(x.AppleMusicUrl))
            // prefer shorter album names, as they are more likely to be the original release instead of
            // a special edition (e.g. "A Cappella Super Deluxe Version")
            .ThenBy(x => x.Album.Length)
            .FirstOrDefault();
    }

    public bool IsNegativeSearchEntry(ArtworkCacheEntry entry)
    {
        return IsNegativeSearchKey(entry.AppleMusicUrl);
    }

    public async Task SaveNegativeSearchResultAsync(string artist, string album)
    {
        ArtworkCacheEntry entry = new(
            AppleMusicUrl: BuildNegativeSearchCacheKey(artist, album),
            Artist: artist,
            Album: album,
            M3u8Url: "NONE",
            M3u8UrlTall: "NONE",
            LastFetched: System.DateTime.UtcNow,
            SearchCount: 1
        );

        await SaveEntryAsync(entry);
    }
    
    public async Task IncrementDownloadCountAsync(string m3U8Url)
    {
        await _fileLock.WaitAsync();
        try
        {
            var target = _cache.FirstOrDefault(x => x.Value.M3u8Url == m3U8Url);
            
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
    
    public async Task IncrementSearchCountAsync(ArtworkCacheEntry cacheEntry)
    {
        await _fileLock.WaitAsync();
        try
        {
            if (_cache.TryGetValue(cacheEntry.AppleMusicUrl, out var entry))
            {
                var updatedEntry = entry with { SearchCount = entry.SearchCount + 1 };
                _cache[cacheEntry.AppleMusicUrl] = updatedEntry;
                
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
            .Where(x => (x.M3u8Url != null && x.M3u8Url != "NONE") || (x.M3u8UrlTall != null && x.M3u8UrlTall != "NONE"))
            .OrderByDescending(x => x.LastFetched)
            .Take(limit);
    }
}