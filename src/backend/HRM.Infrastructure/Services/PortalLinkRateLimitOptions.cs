namespace HRM.Infrastructure.Services;

/// <summary>
/// DF-7 (US-REC-008 NFR-6, ISSUE-130): limits for the per-IP applicant-portal magic-link throttle. Bound from the
/// <c>Recruitment:PortalLink</c> configuration section and shared by BOTH the Redis and DB-fallback
/// <see cref="Application.Common.Interfaces.IPortalLinkIpRateLimiter"/> implementations so they enforce identical
/// caps. The defaults preserve the historical hardcoded behaviour (10 issues per IP per hour), so a missing config
/// section changes nothing.
/// </summary>
public sealed class PortalLinkRateLimitOptions
{
    public const string SectionName = "Recruitment:PortalLink";

    /// <summary>Max tokens a single IP may issue (across all applicant emails) within <see cref="IpWindowSeconds"/>.</summary>
    public int MaxIssuesPerIp { get; set; } = 10;

    /// <summary>The per-IP rate-limit window, in seconds (default 3600 = 1 hour).</summary>
    public int IpWindowSeconds { get; set; } = 3600;
}
