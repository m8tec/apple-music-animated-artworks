public enum AppleMusicPageParseStatus
{
    Success,
    RateLimited,
    Error
}

public readonly record struct AppleMusicPageParseResult(
    AppleMusicPageParseStatus Status,
    string? UrlSquare = null,
    string? UrlTall = null,
    string Artist = "Unknown Artist",
    string Album = "Unknown Album"
);