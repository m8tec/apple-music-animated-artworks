
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AnimatedArtworks.Domain;

namespace AnimatedArtworks.Infrastructure;
public partial class AppleMusicClient(HttpClient httpClient) : IAppleMusicClient
{
    [GeneratedRegex(@"<amp-ambient-video[^>]*?src=""([^""]+\.m3u8)""", RegexOptions.IgnoreCase)]
    private partial Regex AmpVideoRegex();

    public async Task<string?> FetchAnimatedArtworkUrlAsync(string artist, string album, CancellationToken ct)
    {
        var musicWebUrl = await GetAppleMusicUrlAsync(artist, album, ct);
        if (string.IsNullOrEmpty(musicWebUrl)) return null;

        var htmlContent = await httpClient.GetStringAsync(musicWebUrl, ct);
        
        var match = AmpVideoRegex().Match(htmlContent);
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<string?> GetAppleMusicUrlAsync(string artist, string album, CancellationToken ct)
    {
        var query = Uri.EscapeDataString($"{artist} {album}");
        var searchUrl = $"https://itunes.apple.com/search?term={query}&entity=album&limit=5";

        try
        {
            var response = await httpClient.GetAsync(searchUrl, ct);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync(ct);
            var json = JsonNode.Parse(jsonString);

            var results = json?["results"]?.AsArray();
            if (results != null && results.Count > 0)
            {
                var collectionViewUrl = results[0]?["collectionViewUrl"]?.ToString();

                if (!string.IsNullOrEmpty(collectionViewUrl))
                {
                   var uri = new Uri(collectionViewUrl);
                   return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
                }
            }
        }
        catch (HttpRequestException)
        {
        }

        return null;
    }
}