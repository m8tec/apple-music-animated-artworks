using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace AnimatedArtworks.Infrastructure;
public partial class AppleMusicClient(HttpClient httpClient, SystemStatusService statusService) : IAppleMusicClient
{
    [GeneratedRegex(@"<script[^>]+type=""application/ld\+json""[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private partial Regex JsonLdRegex();

    [GeneratedRegex(@"<amp-ambient-video[^>]*?src=""([^""]+\.m3u8)""", RegexOptions.IgnoreCase)]
    private partial Regex AmpVideoRegex();
    
    [GeneratedRegex(@"href=""(https://music\.apple\.com/[a-z]{2}/album/[^/""?]+/\d+)""", RegexOptions.IgnoreCase)]
    private partial Regex AppleMusicLinkRegex();

    private const string AppleMusicSearchUrl = "https://music.apple.com/us/search?term=";
    private const string ItunesSearchUrl = "https://itunes.apple.com/search";
    
    public async Task<string?> GetAppleMusicUrlViaWebSearchAsync(string artist, string album, string? title = null, CancellationToken ct = default)
    {
        List<string> searchParts = [artist, album];
        if (!string.IsNullOrWhiteSpace(title)) searchParts.Add(title);

        string query = Uri.EscapeDataString(string.Join(" ", searchParts));

        string searchUrl = AppleMusicSearchUrl + query;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
            
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
            {
                Log.Warning("Rate Limit hit on Apple Music: {StatusCode}", response.StatusCode);
                statusService.ReportRateLimit();
                return null;
            }
            
            response.EnsureSuccessStatusCode();
            
            statusService.ReportSuccess();

            string htmlContent = await response.Content.ReadAsStringAsync(ct);
            
            Match match = AppleMusicLinkRegex().Match(htmlContent);

            if (match.Success)
            {
                string foundUrl = match.Groups[1].Value;
                Log.Debug("Found album: {Url} for query: {Query}", foundUrl, query);
                return foundUrl;
            }
            
            Log.Debug("Found no album links in HTML for query: {Query}", query);
        }
        catch (Exception ex)
        {
            Log.Error("Apple Music Search Scrape failed: {Message}", ex.Message);
        }

        return null;
    }

    public async Task<string?> GetAppleMusicUrlViaItunesAsync(string artist, string album, string? title = null, CancellationToken ct = default)
    {
        List<string> searchParts = [artist, album];
        if (!string.IsNullOrWhiteSpace(title)) searchParts.Add(title);
        
        string query = Uri.EscapeDataString(string.Join(" ", searchParts));
        
        string entity = string.IsNullOrWhiteSpace(title) ? "album" : "song";
        
        string searchUrl = ItunesSearchUrl + $"?term={query}&entity={entity}&limit=5&explicit=Yes";

        try
        {
            HttpResponseMessage response = await httpClient.GetAsync(searchUrl, ct);
            
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
            {
                Log.Warning("Rate Limit hit on iTunes API: {StatusCode}", response.StatusCode);
                statusService.ReportRateLimit();
                return null;
            }
            
            response.EnsureSuccessStatusCode();
            
            statusService.ReportSuccess();

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
        catch (HttpRequestException ex)
        {
            Log.Error("iTunes API request failed: {Message}", ex.Message);
        }

        return null;
    }

    public async Task<(string? M3u8Url, string Artist, string Album)> ParseAppleMusicPageAsync(string url, CancellationToken ct)
    {
        try 
        {
            HttpResponseMessage response = await httpClient.GetAsync(url, ct);
            
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
            {
                Log.Warning("Rate Limit hit while parsing album page: {StatusCode}", response.StatusCode);
                statusService.ReportRateLimit();
                return (null, "Unknown Artist", "Unknown Album");
            }

            response.EnsureSuccessStatusCode();
            statusService.ReportSuccess();

            string htmlContent = await response.Content.ReadAsStringAsync(ct);
            
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
                        Log.Error("Failed to parse JSON-LD for album info.");
                    }
                }
            }

            return (m3u8Url, artistName, albumName);
        }
        catch (HttpRequestException ex)
        {
            Log.Error("Network error in ParseAppleMusicPageAsync: {Message}", ex.Message);
            return (null, "Unknown Artist", "Unknown Album");
        }
    }
}