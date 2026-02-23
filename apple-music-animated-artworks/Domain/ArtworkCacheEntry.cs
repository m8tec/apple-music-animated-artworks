public record ArtworkCacheEntry(
    string AppleMusicUrl,
    string Artist,
    string Album,
    string? M3u8Url,
    DateTime LastFetched
);