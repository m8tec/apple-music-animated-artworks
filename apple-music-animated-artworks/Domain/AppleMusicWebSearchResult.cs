public enum AppleMusicWebSearchStatus
{
    Found,
    NoMatch,
    RateLimited,
    Error
}

public readonly record struct AppleMusicWebSearchResult(
    AppleMusicWebSearchStatus Status,
    string? Url = null
);
