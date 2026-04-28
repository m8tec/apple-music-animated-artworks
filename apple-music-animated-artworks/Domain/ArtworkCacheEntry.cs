using System;

public record ArtworkCacheEntry(
    string AppleMusicUrl,
    string Artist,
    string Album,
    string? M3u8Url,
    string? M3u8UrlTall,
    DateTime LastFetched,
    DateTime LastUpdated = default,
    int DownloadCount = 0,
    int SearchCount = 0
);