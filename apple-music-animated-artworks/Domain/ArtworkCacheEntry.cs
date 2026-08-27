using System;
using System.Text.Json.Serialization;

public record ArtworkCacheEntry(
    string AppleMusicUrl,
    string Artist,
    string Album,
    string? M3u8Url,
    string? M3u8UrlTall,
    DateTime LastFetched,
    int DownloadCount = 0,
    int SearchCount = 0
)
{
    [JsonIgnore]
    public string NormalizedArtist { get; init; } = string.Empty;

    [JsonIgnore]
    public string NormalizedAlbum { get; init; } = string.Empty;
}