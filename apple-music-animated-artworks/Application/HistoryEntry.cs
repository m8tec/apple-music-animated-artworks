namespace AnimatedArtworks.Application;

using System.Collections.Concurrent;

public record HistoryEntry(string Artist, string Album, string Url, DateTime FetchedAt);

public interface IHistoryService
{
    void AddSuccessfulSearch(string artist, string album, string url);
    IEnumerable<HistoryEntry> GetRecentSearches(int limit = 10);
}

public class MemoryHistoryService : IHistoryService
{
    private readonly ConcurrentQueue<HistoryEntry> _history = new();
    private const int MaxHistorySize = 10;

    public void AddSuccessfulSearch(string artist, string album, string url)
    {
        _history.Enqueue(new HistoryEntry(artist, album, url, DateTime.UtcNow));

        while (_history.Count > MaxHistorySize)
        {
            _history.TryDequeue(out _);
        }
    }

    public IEnumerable<HistoryEntry> GetRecentSearches(int limit = 10)
    {
        return _history.Reverse().Take(limit);
    }
}