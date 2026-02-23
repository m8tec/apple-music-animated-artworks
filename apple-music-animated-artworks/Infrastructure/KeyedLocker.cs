namespace AnimatedArtworks.Infrastructure;

using System.Collections.Concurrent;

public class KeyedLocker
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    public SemaphoreSlim GetLock(string key)
    {
        return _semaphores.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }
}