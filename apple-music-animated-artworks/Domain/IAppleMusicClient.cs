using System.Threading;
using System.Threading.Tasks;

public interface IAppleMusicClient
{
    Task<string?> GetAppleMusicUrlViaItunesAsync(string artist, string album, string? title = null,
        CancellationToken ct = default);

    Task<AppleMusicWebSearchResult> GetAppleMusicUrlViaWebSearchAsync(string artist, string album, string? title = null,
        CancellationToken ct = default);
    
    Task<AppleMusicPageParseResult> ParseAppleMusicPageAsync(string url, CancellationToken ct);
}