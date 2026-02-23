
using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AnimatedArtworks.Infrastructure;
public partial class AppleMusicClient(HttpClient httpClient) : IAppleMusicClient
{
    [GeneratedRegex(@"<script[^>]+type=""application/ld\+json""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private partial Regex JsonLdRegex();

    [GeneratedRegex(@"<amp-ambient-video[^>]*?src=""([^""]+\.m3u8)""", RegexOptions.IgnoreCase)]
    private partial Regex AmpVideoRegex();

    public async Task<string?> GetAppleMusicUrlAsync(string artist, string album, CancellationToken ct)
    {
        string query = Uri.EscapeDataString($"{artist} {album}");
        string searchUrl = $"https://itunes.apple.com/search?term={query}&entity=album&limit=5&explicit=Yes";

        try
        {
            HttpResponseMessage response = await httpClient.GetAsync(searchUrl, ct);
            response.EnsureSuccessStatusCode();

            string jsonString = await response.Content.ReadAsStringAsync(ct);
            JsonNode? json = JsonNode.Parse(jsonString);

            JsonArray? results = json?["results"]?.AsArray();
            if (results is { Count: > 0 })
            {
                string? collectionViewUrl = results[0]?["collectionViewUrl"]?.ToString();

                if (!string.IsNullOrEmpty(collectionViewUrl))
                {
                   Uri uri = new(collectionViewUrl);
                   return $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";
                }
            }
        }
        catch (HttpRequestException)
        {
        }

        return null;
    }

    public async Task<(string? M3u8Url, string Artist, string Album)> ParseAppleMusicPageAsync(string url, CancellationToken ct)
    {
        string htmlContent = await httpClient.GetStringAsync(url, ct);
        
        Match m3u8Match = AmpVideoRegex().Match(htmlContent);
        string? m3u8Url = m3u8Match.Success ? m3u8Match.Groups[1].Value : null;

        string artistName = "Unknown Artist";
        string albumName = "Unknown Album";

        MatchCollection jsonMatches = JsonLdRegex().Matches(htmlContent);
        foreach (Match match in jsonMatches)
        {
            if (match.Success)
            {
                try
                {
                    string jsonString = match.Groups[1].Value;
                    JsonNode? json = JsonNode.Parse(jsonString);

                    if (json?["@type"]?.ToString() == "MusicAlbum")
                    {
                        JsonNode? nameNode = json["name"];
                        if (nameNode != null) albumName = nameNode.ToString();

                        JsonArray? byArtistArray = json["byArtist"]?.AsArray();
                        if (byArtistArray is { Count: > 0 })
                        {
                            JsonNode? artistNode = byArtistArray[0]?["name"];
                            if (artistNode != null) artistName = artistNode.ToString();
                        }
                        
                        break;
                    }
                }
                catch
                {
                }
            }
        }

        return (m3u8Url, artistName, albumName);
    }
}