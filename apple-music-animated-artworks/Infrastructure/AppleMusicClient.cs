using System;
using System.Collections.Generic;
using System.Linq;
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
    [GeneratedRegex(@"music\.apple\.com/(?:([a-z]{2})/)?album/(?:[^/]+/)?(\d+)", RegexOptions.IgnoreCase)]
    private partial Regex StorefrontAlbumRegex();

    [GeneratedRegex(@"(/assets/[^""]+\.js)", RegexOptions.IgnoreCase)]
    private partial Regex JsAssetRegex();

    [GeneratedRegex(@"[""'](eyJ[A-Za-z0-9-_=]+\.[A-Za-z0-9-_=]+\.[A-Za-z0-9-_=]+)[""']")]
    private partial Regex JwtRegex();
    
    [GeneratedRegex(@"href=""(https://music\.apple\.com/[a-z]{2}/album/([^/""?]+)/\d+)""", RegexOptions.IgnoreCase)]
    private partial Regex AppleMusicLinkRegex();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex NonAlphanumericRegex();

    private const string AppleMusicSearchUrl = "https://music.apple.com/us/search?term=";
    private const string ItunesSearchUrl = "https://itunes.apple.com/search";
    
    public async Task<AppleMusicWebSearchResult> GetAppleMusicUrlViaWebSearchAsync(string artist, string album, string? title = null, CancellationToken ct = default)
    {
        List<string> searchParts = [artist, album];
        if (!string.IsNullOrWhiteSpace(title)) searchParts.Add(title);

        string query = Uri.EscapeDataString(string.Join(" ", searchParts));

        string searchUrl = AppleMusicSearchUrl + query;

        try
        {
            Log.Information("Outgoing request to Apple Music search: {SearchUrl}", searchUrl);
            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            using HttpResponseMessage response = await httpClient.SendAsync(request, ct);

            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
            {
                Log.Warning("Rate Limit hit on Apple Music: {StatusCode}", response.StatusCode);
                statusService.ReportRateLimit();
                return new(AppleMusicWebSearchStatus.RateLimited);
            }
            
            response.EnsureSuccessStatusCode();
            
            statusService.ReportSuccess();

            string htmlContent = await response.Content.ReadAsStringAsync(ct);
            
            MatchCollection matches = AppleMusicLinkRegex().Matches(htmlContent);

            foreach (Match match in matches)
            {
                string foundUrl = match.Groups[1].Value;
                string foundAlbumSlug = Uri.UnescapeDataString(match.Groups[2].Value).Replace('-', ' ');

                if (ContainsAlbumName(foundAlbumSlug, album))
                {
                    Log.Debug("Found matching album: {Url} for query: {Query}", foundUrl, query);
                    return new(AppleMusicWebSearchStatus.Found, foundUrl);
                }

                Log.Debug("Rejected non-matching album hit. Requested: {RequestedAlbum}, Found: {FoundAlbum}, Url: {Url}", album, foundAlbumSlug, foundUrl);
            }
            
            Log.Debug("Found no matching album links in HTML for query: {Query}", query);
            return new(AppleMusicWebSearchStatus.NoMatch);
        }
        catch (Exception ex)
        {
            Log.Error("Apple Music Search Scrape failed: {Message}", ex.Message);
            return new(AppleMusicWebSearchStatus.Error);
        }
    }

    private static bool ContainsAlbumName(string foundAlbumName, string requestedAlbumName)
    {
        string normalizedFound = NormalizeForComparison(foundAlbumName);
        string normalizedRequested = NormalizeForComparison(requestedAlbumName);

        if (string.IsNullOrWhiteSpace(normalizedFound) || string.IsNullOrWhiteSpace(normalizedRequested))
        {
            return false;
        }

        return normalizedFound.Contains(normalizedRequested, StringComparison.Ordinal);
    }

    private static string NormalizeForComparison(string input)
    {
        string normalized = input.Trim().ToLowerInvariant();
        normalized = NonAlphanumericRegex().Replace(normalized, " ");
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();
        return normalized;
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
            Log.Information("Outgoing request to iTunes search: {SearchUrl}", searchUrl);
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

    private async Task<string?> GetBearerTokenAsync(string albumUrl, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, albumUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            
            var html = await response.Content.ReadAsStringAsync(ct);
            var jsPaths = JsAssetRegex().Matches(html)
                .Select(m => m.Groups[1].Value)
                .Where(p => p.Contains("index") || p.Contains("web-client") || p.Contains("apple-music"))
                .Distinct();
                
            foreach (var jsPath in jsPaths)
            {
                var jsUrl = $"https://music.apple.com{jsPath}";
                var jsResponse = await httpClient.GetAsync(jsUrl, ct);
                if (!jsResponse.IsSuccessStatusCode) continue;
                
                var jsText = await jsResponse.Content.ReadAsStringAsync(ct);
                var tokenMatch = JwtRegex().Match(jsText);
                if (tokenMatch.Success)
                {
                    return tokenMatch.Groups[1].Value;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Token extraction failed: {Message}", ex.Message);
        }
        return null;
    }

    public async Task<(string? M3u8Url, string? M3u8UrlTall, string Artist, string Album)> ParseAppleMusicPageAsync(string url, CancellationToken ct)
    {
        string artistName = "Unknown Artist";
        string albumName = "Unknown Album";

        try 
        {
            var match = StorefrontAlbumRegex().Match(url);
            if (!match.Success) return (null, null, artistName, albumName);

            string storefront = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value : "us";
            string albumId = match.Groups[2].Value;

            string? token = await GetBearerTokenAsync(url, ct);
            if (token == null) return (null, null, artistName, albumName);

            string apiUrl = $"https://amp-api.music.apple.com/v1/catalog/{storefront}/albums/{albumId}?extend=editorialVideo&platform=web";
            
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("Authorization", $"Bearer {token}");
            request.Headers.Add("Origin", "https://music.apple.com");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, ct);
            
            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
            {
                Log.Warning("Rate Limit hit while calling AMP API: {StatusCode}", response.StatusCode);
                statusService.ReportRateLimit();
                return (null, null, artistName, albumName);
            }

            response.EnsureSuccessStatusCode();
            statusService.ReportSuccess();

            var jsonString = await response.Content.ReadAsStringAsync(ct);
            JsonNode? json = JsonNode.Parse(jsonString);
            
            var attrs = json?["data"]?[0]?["attributes"];
            if (attrs != null)
            {
                if (attrs["artistName"] != null) artistName = attrs["artistName"]!.ToString();
                if (attrs["name"] != null) albumName = attrs["name"]!.ToString();
                
                var videos = attrs["editorialVideo"];
                if (videos != null)
                {
                    string? urlSquare = videos["motionDetailSquare"]?["video"]?.ToString();
                    string? urlTall = videos["motionDetailTall"]?["video"]?.ToString();
                    return (urlSquare, urlTall, artistName, albumName);
                }
            }

            return (null, null, artistName, albumName);
        }
        catch (HttpRequestException ex)
        {
            Log.Error("Network error in ParseAppleMusicPageAsync: {Message}", ex.Message);
            return (null, null, artistName, albumName);
        }
        catch (Exception ex)
        {
            Log.Error("Unexpected error in ParseAppleMusicPageAsync: {Message}", ex.Message);
            return (null, null, artistName, albumName);
        }
    }
}