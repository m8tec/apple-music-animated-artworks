using System.Threading;
using System.Threading.Tasks;

public interface IAppleMusicClient
{
    Task<string?> GetAppleMusicUrlViaItunesAsync(string artist, string album, string? title = null,
        CancellationToken ct = default);

    Task<string?> GetAppleMusicUrlViaWebSearchAsync(string artist, string album, string? title = null,
        CancellationToken ct = default);
    
    Task<(string? M3u8Url, string Artist, string Album)> ParseAppleMusicPageAsync(string url, CancellationToken ct);
}