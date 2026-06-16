using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Auto-closes unsigned performance reviews (US-PRF-006 BR-3). Invoked once per active tenant by the Hangfire
/// recurring job (which sets the tenant context first). For the current tenant it finds reviews still
/// PendingEmployeeSignOff whose <c>SignoffRequestedAt</c> is older than the cycle's tenant-configurable window
/// (default 7 days), flips them to NoResponse, appends an IMMUTABLE AutoClosedNoResponse sign-off (system
/// actor, no IP), and notifies HR. All queries ride the EF global query filter, so the sweep never crosses
/// tenants (NFR-2). Mirrors <c>Feedback360ReminderService</c> / <c>SelfAssessmentReminderService</c>.
/// </summary>
public sealed class ReviewSignoffAutoCloseService : IReviewSignoffAutoCloseService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<ReviewSignoffAutoCloseService> _logger;

    public ReviewSignoffAutoCloseService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IPerformanceNotificationService notifications,
        ILogger<ReviewSignoffAutoCloseService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<int> AutoCloseOverdueAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return 0;

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
            var days = windowByCycle.TryGetValue(review.CycleId, out var d) ? d : 7;
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
