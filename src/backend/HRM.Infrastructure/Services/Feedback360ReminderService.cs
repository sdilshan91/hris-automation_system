using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// 360-degree reviewer-reminder service (US-PRF-005 AC-5/FR-8). Invoked once per active tenant by the
/// Hangfire recurring job (which sets the tenant context first). For the current tenant it finds reviewer
/// assignments still Pending in any Active, 360-enabled cycle whose manager-review window is currently open,
/// and dispatches a reminder via the notification seam. All queries ride the EF global query filter, so the
/// sweep never crosses tenants (NFR-2).
/// </summary>
public sealed class Feedback360ReminderService : IFeedback360ReminderService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<Feedback360ReminderService> _logger;

    public Feedback360ReminderService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IPerformanceNotificationService notifications,
        ILogger<Feedback360ReminderService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<int> SendDueRemindersAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return 0;

        // Active, 360-enabled cycles whose review window is currently open (the 360 phase runs alongside the
        // manager-review window).
        var cycles = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .Where(c => c.Status == AppraisalCycleStatus.Active && c.Is360Enabled)
            .Select(c => new { c.Id, c.ManagerReviewStart, c.ManagerReviewEnd })
            .ToListAsync(cancellationToken);

        var openCycleIds = cycles
            .Where(c => nowUtc >= c.ManagerReviewStart && nowUtc <= c.ManagerReviewEnd)
            .Select(c => c.Id)
            .ToHashSet();

        if (openCycleIds.Count == 0)
            return 0;

        var pending = await _dbContext.ReviewerAssignments
            .AsNoTracking()
            .Where(a => a.Status == ReviewerAssignmentStatus.Pending && openCycleIds.Contains(a.CycleId))
            .Select(a => new { a.ReviewerEmployeeId, a.RevieweeEmployeeId, a.CycleId })
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var a in pending)
        {
            await _notifications.NotifyReviewerReminderAsync(
                a.ReviewerEmployeeId, a.RevieweeEmployeeId, a.CycleId, cancellationToken);
            sent++;
        }

        _logger.LogInformation(
            "360 reviewer reminders dispatched. Count={Count}, TenantId={TenantId}", sent, _tenantContext.TenantId);
        return sent;
    }
}
