using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HRM.Infrastructure.Services;

/// <summary>
/// DF-7: single-instance fallback implementation of <see cref="IPortalLinkIpRateLimiter"/>, used when Redis is NOT
/// configured. Preserves the original (ISSUE-130) precise SLIDING-window behaviour: it counts the portal tokens this
/// IP has actually persisted within the trailing window over <c>applicant_portal_token</c> and allows only while the
/// count is under the cap. Keying is per-(tenant, IP): the EF global query filter scopes the count to the current
/// tenant, and the <c>RequestIp</c> predicate scopes it to the IP.
///
/// <para>This is the correct, precise counter for a single instance; the trade vs the Redis fixed-window is only that
/// it does not aggregate across instances (each instance sees only its own DB, but they share one DB, so a shared-DB
/// deployment still counts correctly — the Redis path exists for the counter's O(1) hot path and to avoid a COUNT per
/// request at scale).</para>
/// </summary>
public sealed class DbCountPortalLinkIpRateLimiter : IPortalLinkIpRateLimiter
{
    private readonly AppDbContext _dbContext;
    private readonly PortalLinkRateLimitOptions _options;
    private readonly TimeProvider _clock;

    public DbCountPortalLinkIpRateLimiter(
        AppDbContext dbContext,
        IOptions<PortalLinkRateLimitOptions> options,
        TimeProvider? clock = null)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<bool> TryAcquireAsync(Guid tenantId, string ip, CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;

        // Sliding window: how many tokens has this IP issued (across all emails) in the trailing window? The global
        // query filter scopes this to the current tenant (per-(tenant, IP) keying, AC-4).
        var ipIssueCount = await _dbContext.ApplicantPortalTokens
            .AsNoTracking()
            .CountAsync(t => t.RequestIp == ip
                             && t.CreatedAt > now.AddSeconds(-_options.IpWindowSeconds), ct);

        // At/over the cap → block; under it → allow (identical to the original inline guard's semantics).
        return ipIssueCount < _options.MaxIssuesPerIp;
    }
}
