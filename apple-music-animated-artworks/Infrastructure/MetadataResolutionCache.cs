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

public sealed class MetadataResolutionCache : IAsyncDisposable
{
    private readonly string _filePath;
    private readonly ConcurrentDictionary<string, MetadataResolutionEntry> _cache = new();
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _flushLoop;
    private volatile bool _isDirty;
    public bool IsInitialized { get; private set; }

    public MetadataResolutionCache(string filePath)
    {
        _filePath = filePath;
        _flushLoop = FlushLoopAsync(_shutdown.Token);
    }

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        var entries = await AtomicJsonFileStore.ReadAndDeserializeWithBackupAsync<List<MetadataResolutionEntry>>(_filePath, cancellationToken).ConfigureAwait(false) ?? [];
        foreach (var entry in entries)
        {
            _cache[BuildKey(entry.Artist, entry.Album)] = entry;
        }

        IsInitialized = true;
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();

        try
        {
            await _flushLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when shutting down.
        }

        _fileLock.Dispose();
        _shutdown.Dispose();
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
                _isDirty = true;
                return new(MetadataResolutionStatus.None);
            }

            return new(MetadataResolutionStatus.Resolved, entry.ResolvedAppleMusicUrl);
        }

        return new(MetadataResolutionStatus.None);
    }

    public Task SaveResolvedUrlAsync(string artist, string album, string resolvedAppleMusicUrl)
    {
        var entry = new MetadataResolutionEntry(
            Artist: artist,
            Album: album,
            ResolvedAppleMusicUrl: resolvedAppleMusicUrl,
            Status: MetadataResolutionStatus.Resolved,
            LastResolved: DateTime.UtcNow
        );

        _cache[BuildKey(artist, album)] = entry;
        _isDirty = true;
        return Task.CompletedTask;
    }

    public Task SaveNoMatchAsync(string artist, string album)
    {
        var entry = new MetadataResolutionEntry(
            Artist: artist,
            Album: album,
            ResolvedAppleMusicUrl: null,
            Status: MetadataResolutionStatus.NoMatch,
            LastResolved: DateTime.UtcNow
        );

        _cache[BuildKey(artist, album)] = entry;
        _isDirty = true;
        return Task.CompletedTask;
    }

    public Task RemoveResolvedUrlAsync(string artist, string album)
    {
        _cache.TryRemove(BuildKey(artist, album), out _);
        _isDirty = true;
        return Task.CompletedTask;
    }

    private async Task FlushLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await PersistIfDirtyAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown is in progress.
        }
    }

    private async Task PersistIfDirtyAsync(CancellationToken cancellationToken)
    {
        if (!_isDirty || !await _fileLock.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            if (!_isDirty)
            {
                return;
            }

            await AtomicJsonFileStore.WriteAtomicallyAsync(_filePath, _cache.Values, cancellationToken).ConfigureAwait(false);
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
