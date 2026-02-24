using System;

public record ArtworkCacheEntry(
    string AppleMusicUrl,
    string Artist,
    string Album,
    string? M3u8Url,
    DateTime LastFetched,
    int DownloadCount = 0,
    int SearchCount = 0
);