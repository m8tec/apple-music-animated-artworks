
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

    public async Task<(string? M3u8Url, string Artist, string Album)> ParseAppleMusicPageAsync(string url, CancellationToken ct)
    {
        var htmlContent = await httpClient.GetStringAsync(url, ct);
        
        var m3u8Match = AmpVideoRegex().Match(htmlContent);
        var m3u8Url = m3u8Match.Success ? m3u8Match.Groups[1].Value : null;

        string artistName = "Unknown Artist";
        string albumName = "Unknown Album";

        var jsonMatches = JsonLdRegex().Matches(htmlContent);
        foreach (Match match in jsonMatches)
        {
            if (match.Success)
            {
                try
                {
                    var jsonString = match.Groups[1].Value;
                    var json = JsonNode.Parse(jsonString);

                    if (json?["@type"]?.ToString() == "MusicAlbum")
                    {
                        var nameNode = json["name"];
                        if (nameNode != null) albumName = nameNode.ToString();

                        var byArtistArray = json["byArtist"]?.AsArray();
                        if (byArtistArray != null && byArtistArray.Count > 0)
                        {
                            var artistNode = byArtistArray[0]?["name"];
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