using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace AnimatedArtworks.Infrastructure;

public class JsonCacheService : IDisposable
{
    private string FilePath { get; }
    private readonly ConcurrentDictionary<string, ArtworkCacheEntry> _cache = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _flushLoop;

    private volatile bool _isDirty;

    public JsonCacheService(string filePath)
    {
        FilePath = filePath;

        if (File.Exists(FilePath))
        {
            LoadFromDisk();
        }

        _flushLoop = FlushLoopAsync(_shutdown.Token);
    }

    public void Dispose()
    {
        _shutdown.Cancel();

        try
        {
            _flushLoop.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Expected when shutting down.
        }

        _fileLock.Dispose();
        _shutdown.Dispose();
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
            .OrderByDescending(x => (x.M3u8Url != null && x.M3u8Url != "NONE") || (x.M3u8UrlTall != null && x.M3u8UrlTall != "NONE"))
            // prefer shorter album names, as they are more likely to be the original release instead of
            // a special edition (e.g. "A Cappella Super Deluxe Version")
            .ThenBy(x => x.Album.Length)
            .FirstOrDefault();
    }

    public Task IncrementDownloadCountAsync(string m3U8Url)
    {
        var target = _cache.FirstOrDefault(x => x.Value.M3u8Url == m3U8Url);

        if (target.Key != null)
        {
            var entry = target.Value;
            var updatedEntry = entry with { DownloadCount = entry.DownloadCount + 1 };
            _cache[target.Key] = updatedEntry;
            _isDirty = true;
        }

        return Task.CompletedTask;
    }

    public Task IncrementSearchCountAsync(ArtworkCacheEntry cacheEntry)
    {
        if (_cache.TryGetValue(cacheEntry.AppleMusicUrl, out var entry))
        {
            var updatedEntry = entry with { SearchCount = entry.SearchCount + 1 };
            _cache[cacheEntry.AppleMusicUrl] = updatedEntry;
            _isDirty = true;
        }

        return Task.CompletedTask;
    }

    public async Task SaveEntryAsync(ArtworkCacheEntry newEntry)
    {
        _cache[newEntry.AppleMusicUrl] = newEntry;
        _isDirty = true;
        await SaveIfDirtyAsync().ConfigureAwait(false);
    }

    private async Task FlushLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await SaveIfDirtyAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown is in progress.
        }
    }

    private async Task SaveIfDirtyAsync(CancellationToken cancellationToken = default)
    {
        if (!_isDirty)
        {
            return;
        }

        if (!await _fileLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (!_isDirty)
            {
                return;
            }

            await AtomicJsonFileStore.WriteAtomicallyAsync(FilePath, _cache.Values, cancellationToken).ConfigureAwait(false);
            _isDirty = false;
        }
        catch
        {
            _isDirty = true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private void LoadFromDisk()
    {
        var json = AtomicJsonFileStore.ReadTextWithBackup(FilePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var entries = JsonSerializer.Deserialize<List<ArtworkCacheEntry>>(json) ?? new();
        foreach (var entry in entries)
        {
            _cache[entry.AppleMusicUrl] = entry;
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