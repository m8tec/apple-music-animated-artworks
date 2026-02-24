using System;

namespace AnimatedArtworks.Infrastructure;

public class SystemStatusService
{
    public bool IsRateLimited { get; set; } = false;
    public DateTime LastErrorTime { get; set; } = DateTime.MinValue;

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
}