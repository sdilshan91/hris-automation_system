using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Self-assessment deadline-reminder service (US-PRF-002 FR-7/AC-5). Invoked once per active tenant by the
/// Hangfire recurring job (which sets the tenant context first). For the current tenant it finds employees
/// with goals in any Active cycle whose self-assessment deadline is exactly one of the configured day
/// thresholds away and who have NOT submitted, and dispatches a reminder via the notification seam. All
/// queries ride the EF global query filter, so the sweep never crosses tenants (NFR-2).
/// </summary>
public sealed class SelfAssessmentReminderService : ISelfAssessmentReminderService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<SelfAssessmentReminderService> _logger;

    public SelfAssessmentReminderService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IPerformanceNotificationService notifications,
        ILogger<SelfAssessmentReminderService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<int> SendDueRemindersAsync(DateTime todayUtc, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return 0;

        var thresholds = ISelfAssessmentReminderService.DefaultThresholds;
        var today = todayUtc.Date;

        // Active cycles whose self-assessment deadline (date) is exactly N days out for some threshold N.
        var targetDates = thresholds.Select(d => today.AddDays(d)).ToHashSet();

        var cycles = await _dbContext.AppraisalCycles
            .AsNoTracking()
            .Where(c => c.Status == AppraisalCycleStatus.Active)
            .Select(c => new { c.Id, c.SelfAssessmentEnd })
            .ToListAsync(cancellationToken);

        var dueCycles = cycles
            .Select(c => new { c.Id, Deadline = c.SelfAssessmentEnd.Date })
            .Where(c => targetDates.Contains(c.Deadline))
            .ToList();

        if (dueCycles.Count == 0)
            return 0;

        var sent = 0;
        foreach (var cycle in dueCycles)
        {
            var daysOut = (cycle.Deadline - today).Days;

            // Distinct employees with at least one goal in this cycle.
            var employeeIds = await _dbContext.Goals
                .AsNoTracking()
                .Where(g => g.CycleId == cycle.Id)
                .Select(g => g.EmployeeId)
                .Distinct()
                .ToListAsync(cancellationToken);
            if (employeeIds.Count == 0)
                continue;

            // Employees who have already SUBMITTED for this cycle — exclude them.
            var submitted = await _dbContext.SelfAssessments
                .AsNoTracking()
                .Where(s => s.CycleId == cycle.Id && s.Status == SelfAssessmentStatus.Submitted)
                .Select(s => s.EmployeeId)
                .ToListAsync(cancellationToken);
            var submittedSet = submitted.ToHashSet();

            foreach (var employeeId in employeeIds)
            {
                if (submittedSet.Contains(employeeId))
                    continue;
                await _notifications.NotifySelfAssessmentReminderAsync(employeeId, cycle.Id, daysOut, cancellationToken);
                // ISSUE-106 (US-PRF-004 FR-7/AC-5): record a queryable tenant audit row for each reminder
                // dispatched. This is a system/background sweep (no user actor ⇒ UserId is null); TenantId is
                // stamped from the resolved tenant the recurring job set before invoking this service.
                AddReminderAudit(employeeId, cycle.Id, daysOut);
                sent++;
            }
        }

        if (sent > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Self-assessment reminders dispatched. Count={Count}, TenantId={TenantId}", sent, _tenantContext.TenantId);
        return sent;
    }

    private static readonly JsonSerializerOptions AuditJsonOptions = new() { WriteIndented = false };

    /// <summary>
    /// ISSUE-106 (US-PRF-004): appends a queryable tenant audit row for a dispatched self-assessment
    /// reminder. Background sweep ⇒ no user actor (UserId null); TenantId is stamped from the resolved tenant
    /// context. Added to the change tracker and persisted by the single SaveChanges at the end of the sweep.
    /// </summary>
    private void AddReminderAudit(Guid employeeId, Guid cycleId, int daysOut)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = null,
            EventType = "SelfAssessment.ReminderSent",
            Action = "SelfAssessment.ReminderSent",
            ResourceType = "SelfAssessment",
            ResourceId = employeeId.ToString(),
            After = JsonSerializer.Serialize(new { employeeId, cycleId, daysOut }, AuditJsonOptions),
            Detail = $"Self-assessment reminder sent to employee {employeeId} for cycle {cycleId} ({daysOut} day(s) before deadline).",
            CreatedAt = DateTime.UtcNow,
        });
    }
}
