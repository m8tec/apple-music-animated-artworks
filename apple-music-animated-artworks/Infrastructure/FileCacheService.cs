namespace AnimatedArtworks.Infrastructure;

public class FileCacheService : ICacheService
{
    private readonly string _cacheDirectory;

    public FileCacheService()
    {
        _cacheDirectory = Path.Combine(Directory.GetCurrentDirectory(), "cache_data");
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct)
    {
        var filePath = Path.Combine(_cacheDirectory, $"{key}.txt");
        if (!File.Exists(filePath)) return default;

        var content = await File.ReadAllTextAsync(filePath, ct);
        return (T)(object)content; 
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken ct)
    {
        var filePath = Path.Combine(_cacheDirectory, $"{key}.txt");
        if (value != null)
        {
            await File.WriteAllTextAsync(filePath, value.ToString(), ct);
        }
    }
}