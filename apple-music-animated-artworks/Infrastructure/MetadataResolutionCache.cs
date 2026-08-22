using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnimatedArtworks.Infrastructure;

public sealed class MetadataResolutionCache : IDisposable
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, MetadataResolutionEntry> _cache = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly object _persistLock = new();
    private Task _pendingPersist = Task.CompletedTask;

    public MetadataResolutionCache(string filePath)
    {
        _filePath = filePath;

        if (!File.Exists(_filePath))
        {
            return;
        }

        var json = AtomicJsonFileStore.ReadTextWithBackup(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        var entries = JsonSerializer.Deserialize<List<MetadataResolutionEntry>>(json) ?? [];
        foreach (var entry in entries)
        {
            _cache[BuildKey(entry.Artist, entry.Album)] = entry;
        }
    }

    public void Dispose()
    {
        _fileLock.Dispose();
    }

    public MetadataResolutionLookup GetLookup(string artist, string album, TimeSpan noMatchTtl)
    {
        if (_cache.TryGetValue(BuildKey(artist, album), out var entry))
        {
            if (entry.Status == MetadataResolutionStatus.NoMatch)
            {
                bool isFreshNoMatch = DateTime.UtcNow - entry.LastResolved <= noMatchTtl;
                if (isFreshNoMatch)
                {
                    return new(MetadataResolutionStatus.NoMatch);
                }

                _cache.TryRemove(BuildKey(artist, album), out _);
                _ = QueuePersistAsync();
                return new(MetadataResolutionStatus.None);
            }

            return new(MetadataResolutionStatus.Resolved, entry.ResolvedAppleMusicUrl);
        }

        return new(MetadataResolutionStatus.None);
    }

    public async Task SaveResolvedUrlAsync(string artist, string album, string resolvedAppleMusicUrl)
    {
        var entry = new MetadataResolutionEntry(
            Artist: artist,
            Album: album,
            ResolvedAppleMusicUrl: resolvedAppleMusicUrl,
            Status: MetadataResolutionStatus.Resolved,
            LastResolved: DateTime.UtcNow
        );

        _cache[BuildKey(artist, album)] = entry;
        await QueuePersistAsync().ConfigureAwait(false);
    }

    public async Task SaveNoMatchAsync(string artist, string album)
    {
        var entry = new MetadataResolutionEntry(
            Artist: artist,
            Album: album,
            ResolvedAppleMusicUrl: null,
            Status: MetadataResolutionStatus.NoMatch,
            LastResolved: DateTime.UtcNow
        );

        _cache[BuildKey(artist, album)] = entry;
        await QueuePersistAsync().ConfigureAwait(false);
    }

    public async Task RemoveResolvedUrlAsync(string artist, string album)
    {
        _cache.TryRemove(BuildKey(artist, album), out _);
        await QueuePersistAsync().ConfigureAwait(false);
    }

    private Task QueuePersistAsync()
    {
        lock (_persistLock)
        {
            if (_pendingPersist.IsCompleted)
            {
                _pendingPersist = PersistAsync();
            }

            return _pendingPersist;
        }
    }

    private async Task PersistAsync()
    {
        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(_cache.Values, options);
            await AtomicJsonFileStore.WriteAtomicallyAsync(_filePath, json).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static string BuildKey(string artist, string album)
    {
        return $"{NormalizeForKey(artist)}|{NormalizeForKey(album)}";
    }

    private static string NormalizeForKey(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        return new string(input.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}

public readonly record struct MetadataResolutionEntry(
    string Artist,
    string Album,
    string? ResolvedAppleMusicUrl,
    MetadataResolutionStatus Status,
    DateTime LastResolved
);

public readonly record struct MetadataResolutionLookup(
    MetadataResolutionStatus Status,
    string? ResolvedAppleMusicUrl = null
);

public enum MetadataResolutionStatus
{
    None,
    Resolved,
    NoMatch
}
