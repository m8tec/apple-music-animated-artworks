public interface IAppleMusicClient
{
    Task<string?> GetAppleMusicUrlAsync(string artist, string album, CancellationToken ct);
    
    Task<(string? M3u8Url, string Artist, string Album)> ParseAppleMusicPageAsync(string url, CancellationToken ct);
}