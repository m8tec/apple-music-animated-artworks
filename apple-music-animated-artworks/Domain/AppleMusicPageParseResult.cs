public enum AppleMusicPageParseStatus
{
    Success,
    RateLimited,
    Error
}

public readonly record struct AppleMusicPageParseResult(
    AppleMusicPageParseStatus Status,
    string? M3u8Url = null,
    string Artist = "Unknown Artist",
    string Album = "Unknown Album"
);