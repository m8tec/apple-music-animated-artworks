namespace AnimatedArtworks.Infrastructure;
public class MemoryCacheService(IMemoryCache memoryCache) : ICacheService
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        memoryCache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken ct)
    {
        memoryCache.Set(key, value, expiration);
        return Task.CompletedTask;
    }
}