using System;

namespace AnimatedArtworks.Infrastructure;

public class SystemStatusService
{
    private static readonly TimeSpan RateLimitBackoff = TimeSpan.FromHours(1);

    public bool IsRateLimited { get; private set; }
    public DateTime LastErrorTime { get; private set; } = DateTime.MinValue;

    public void ReportRateLimit()
    {
        IsRateLimited = true;
        LastErrorTime = DateTime.UtcNow;
    }

    public void ReportSuccess()
    {
        if (IsRateLimited)
        {
            IsRateLimited = false;
        }
    }

    public bool IsInBackoff()
    {
        if (!IsRateLimited)
            return false;

        var now = DateTime.UtcNow;
        return (now - LastErrorTime) < RateLimitBackoff;
    }
}