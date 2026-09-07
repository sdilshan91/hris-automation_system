using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Auto-closes unsigned performance reviews (US-PRF-006 BR-3). Invoked once per active tenant by the Hangfire
/// recurring job (which sets the tenant context first). For the current tenant it finds reviews still
/// PendingEmployeeSignOff whose <c>SignoffRequestedAt</c> is older than the cycle's tenant-configurable window
/// (default 7 days), flips them to NoResponse, appends an IMMUTABLE AutoClosedNoResponse sign-off (system
/// actor, no IP), and notifies HR. All queries ride the EF global query filter, so the sweep never crosses
/// tenants (NFR-2). Mirrors <c>Feedback360ReminderService</c> / <c>SelfAssessmentReminderService</c>.
/// <para>
/// ENH-012 — WINDOW PRECEDENCE. Two sources can supply the window; the resolution order is:
/// <list type="number">
///   <item>the <c>Performance:SignoffAutoCloseDaysOverride</c> config key, when present and in range;</item>
///   <item>otherwise the cycle's own <c>SignoffAutoCloseDays</c> (tenant-configured over the cycle API);</item>
///   <item>otherwise <c>AppraisalCycle.DefaultSignoffAutoCloseDays</c> (a review whose cycle row is missing).</item>
/// </list>
/// The override deliberately WINS, for one reason: every cycle row has a non-null window (the column is
/// <c>NOT NULL DEFAULT 7</c>), so an override that only filled a gap would never apply to anything and the
/// seam would be dead code — which is the exact "unreachable and untestable" problem it exists to fix.
/// It does not change production behaviour by accident, because it is absent from every committed
/// <c>appsettings*.json</c>: setting it is a deliberate deployment act, not a default. To make sure it can
/// never be mistaken for a tenant's own setting, applying it emits a WARNING on every sweep. Its accepted
/// range is 0..<c>MaxSignoffAutoCloseDays</c> — WIDER than the tenant-facing 1..365, because 0 ("no grace
/// period, close on the next sweep") is precisely what a test needs to force the window without waiting or
/// back-dating rows. An out-of-range override is ignored (warned) rather than honoured, so a fat-fingered
/// value degrades to the tenant's configured behaviour instead of closing everything.
/// </para>
/// <para>
/// NOTE (ENH, out of scope here): there is still no way to DISABLE auto-close for a cycle. 0 does not mean
/// "off" — see <c>AppraisalCycle.MinSignoffAutoCloseDays</c>. A tenant that wants it off must set 365.
/// </para>
/// </summary>
public sealed class ReviewSignoffAutoCloseService : IReviewSignoffAutoCloseService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPerformanceNotificationService _notifications;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ReviewSignoffAutoCloseService> _logger;

    /// <summary>
    /// ENH-012: ops/test override of the auto-close window, mirroring <c>Recruitment:ScorecardLockPeriodHours</c>.
    /// Absent from every committed appsettings file — see the precedence note on the class.
    /// </summary>
    internal const string OverrideConfigKey = "Performance:SignoffAutoCloseDaysOverride";

    public ReviewSignoffAutoCloseService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IPerformanceNotificationService notifications,
        IConfiguration configuration,
        ILogger<ReviewSignoffAutoCloseService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Reads the ENH-012 window override. Null ⇒ not set, or set to a value outside 0..
    /// <see cref="AppraisalCycle.MaxSignoffAutoCloseDays"/> (which is warned and ignored, so a bad value
    /// falls back to each cycle's own window rather than closing reviews early).
    /// </summary>
    private int? ResolveOverrideDays()
    {
        var configured = _configuration.GetValue<int?>(OverrideConfigKey);
        if (configured is null)
            return null;

        if (configured < 0 || configured > AppraisalCycle.MaxSignoffAutoCloseDays)
        {
            _logger.LogWarning(
                "Ignoring out-of-range {Key}={Value}; using each cycle's configured sign-off auto-close window.",
                OverrideConfigKey, configured.Value);
            return null;
        }

        return configured.Value;
    }

    public async Task<int> AutoCloseOverdueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return 0;

        // ENH-012: the override outranks every per-cycle window (see the precedence note on the class). Warn
        // loudly whenever it bites, so a sweep driven by an ops setting is never read back as tenant config.
        var overrideDays = ResolveOverrideDays();
        if (overrideDays.HasValue)
        {
            _logger.LogWarning(
                "{Key}={Value} is set and OVERRIDES every cycle's configured sign-off auto-close window for "
                + "this sweep. TenantId={TenantId}",
                OverrideConfigKey, overrideDays.Value, _tenantContext.TenantId);
        }

        // Per-cycle auto-close window (BR-3, default 7). Indexed lookup, used to compute the cutoff per review.
        var cycleWindows = await _dbContext.AppraisalCycles.AsNoTracking()
            .Select(c => new { c.Id, c.SignoffAutoCloseDays })
            .ToListAsync(cancellationToken);
        var windowByCycle = cycleWindows.ToDictionary(c => c.Id, c => c.SignoffAutoCloseDays);

        var pending = await _dbContext.ManagerReviews
            .Where(r => r.SignoffStatus == ReviewSignoffStatus.PendingEmployeeSignOff
                        && r.SignoffRequestedAt != null)
            .ToListAsync(cancellationToken);

        var closed = 0;
        foreach (var review in pending)
        {
            var days = overrideDays
                ?? (windowByCycle.TryGetValue(review.CycleId, out var d)
                    ? d
                    : AppraisalCycle.DefaultSignoffAutoCloseDays);
            var cutoff = review.SignoffRequestedAt!.Value.AddDays(days);
            if (nowUtc < cutoff)
                continue;

            review.SignoffStatus = ReviewSignoffStatus.NoResponse;
            review.SignoffCompletedAt = nowUtc;

            _dbContext.ReviewSignoffs.Add(new ReviewSignoff
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                ManagerReviewId = review.Id,
                Party = SignoffParty.Hr,
                Action = SignoffAction.AutoClosedNoResponse,
                SignerName = "System",
                SignerUserId = null,
                SignerEmployeeId = null,
                SignedAt = nowUtc,
                ClientIpAddress = null,
                Comments = $"Auto-closed: no employee response within {days} day(s).",
                IsDeleted = false,
            });

            await _notifications.NotifyReviewAutoClosedAsync(
                review.Id, review.EmployeeId, review.CycleId, cancellationToken);
            closed++;
        }

        if (closed > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Review sign-off auto-close sweep. Closed={Count}, TenantId={TenantId}", closed, _tenantContext.TenantId);
        return closed;
    }
}
