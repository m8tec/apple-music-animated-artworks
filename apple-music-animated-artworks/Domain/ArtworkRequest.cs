namespace AnimatedArtworks.Domain;

public record ArtworkRequest(string Artist, string Album);

public record ArtworkResult(
    string Artist, 
    string Album, 
    string? AnimatedArtworkUrl, 
    bool HasAnimatedArtwork
);

public interface IAppleMusicClient
{
    Task<string?> FetchAnimatedArtworkUrlAsync(string artist, string album, CancellationToken ct);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken ct);
}