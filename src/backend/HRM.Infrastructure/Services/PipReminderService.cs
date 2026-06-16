using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// PIP reminder + not-acknowledged sweep (US-PRF-008 FR-3/BR-4). Invoked once per active tenant by the Hangfire
/// recurring job (which sets the tenant context first). For the CURRENT tenant it:
///   - flags any Active PIP the employee never acknowledged within 5 BUSINESS days of initiation as
///     NotAcknowledged, writing an immutable system NotAcknowledged event (BR-4);
///   - dispatches checkpoint reminders 3 days before each scheduled checkpoint date (FR-3);
///   - dispatches a PIP end-date reminder 3 days before the end date (FR-3);
///   - dispatches overdue-checkpoint alerts when a scheduled checkpoint date has passed and no checkpoint was
///     recorded on/after it (FR-3).
/// All queries ride the EF global query filter, so the sweep never crosses tenants (NFR-2). Mirrors
/// <c>ReviewSignoffAutoCloseService</c>. Notifications go through the existing log-only performance seam.
/// </summary>
public sealed class PipReminderService : IPipReminderService
{
    private const int ReminderLeadDays = 3;       // FR-3: remind 3 days before each checkpoint / end date.
    private const int AckBusinessDayWindow = 5;   // BR-4: acknowledge within 5 business days.

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPerformanceNotificationService _notifications;
    private readonly ILogger<PipReminderService> _logger;

    public PipReminderService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IPerformanceNotificationService notifications,
        ILogger<PipReminderService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<int> RunSweepAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return 0;

        var today = DateOnly.FromDateTime(nowUtc);
        var dispatched = 0;

        var activePips = await _dbContext.Pips
            .Include(p => p.Checkpoints)
            .Where(p => p.Status == PipStatus.Active || p.Status == PipStatus.Extended)
            .ToListAsync(cancellationToken);

        var flaggedAny = false;

        foreach (var pip in activePips)
        {
            // BR-4: not-acknowledged flag after 5 business days from initiation.
            if (pip.AcknowledgementStatus == PipAcknowledgementStatus.Pending && pip.InitiatedAt is { } initiated)
            {
                var deadline = AddBusinessDays(initiated, AckBusinessDayWindow);
                if (nowUtc >= deadline)
                {
                    pip.AcknowledgementStatus = PipAcknowledgementStatus.NotAcknowledged;
                    _dbContext.PipEvents.Add(new PipEvent
                    {
                        Id = BaseEntity.NewUuidV7(),
                        TenantId = _tenantContext.TenantId,
                        PipId = pip.Id,
                        EventType = PipEventType.NotAcknowledged,
                        ActorUserId = null,
                        ActorEmployeeId = null,
                        ActorName = "System",
                        OccurredAt = nowUtc,
                        ClientIpAddress = null,
                        Detail = $"Not acknowledged within {AckBusinessDayWindow} business days.",
                        IsDeleted = false,
                    });
                    await _notifications.NotifyPipEventAsync(
                        "pip-not-acknowledged", pip.Id, pip.EmployeeId, pip.EmployeeId, cancellationToken: cancellationToken);
                    flaggedAny = true;
                    dispatched++;
                }
            }

            // FR-3: end-date reminder 3 days before the end date.
            if (pip.EndDate.DayNumber - today.DayNumber == ReminderLeadDays)
            {
                await _notifications.NotifyPipEventAsync(
                    "pip-end-date-reminder", pip.Id, pip.EmployeeId, pip.EmployeeId,
                    pip.EndDate.ToString("yyyy-MM-dd"), cancellationToken);
                dispatched++;
            }

            // FR-3: checkpoint reminders + overdue alerts.
            foreach (var date in pip.ScheduledCheckpointDates)
            {
                var lead = date.DayNumber - today.DayNumber;
                if (lead == ReminderLeadDays)
                {
                    await _notifications.NotifyPipEventAsync(
                        "pip-checkpoint-reminder", pip.Id, pip.EmployeeId, pip.ManagerEmployeeId ?? pip.EmployeeId,
                        date.ToString("yyyy-MM-dd"), cancellationToken);
                    dispatched++;
                }
                else if (lead < 0)
                {
                    // Overdue: the scheduled date has passed with no checkpoint recorded on/after it.
                    var hasRecorded = pip.Checkpoints.Any(c => !c.IsDeleted && c.CheckpointDate >= date);
                    if (!hasRecorded)
                    {
                        await _notifications.NotifyPipEventAsync(
                            "pip-checkpoint-overdue", pip.Id, pip.EmployeeId, pip.ManagerEmployeeId ?? pip.EmployeeId,
                            date.ToString("yyyy-MM-dd"), cancellationToken);
                        dispatched++;
                    }
                }
            }
        }

        if (flaggedAny)
            await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "PIP reminder sweep. Dispatched={Count}, TenantId={TenantId}", dispatched, _tenantContext.TenantId);
        return dispatched;
    }

    /// <summary>
    /// Adds <paramref name="businessDays"/> working days (Mon–Fri) to a UTC instant (BR-4). Public holidays are
    /// out of scope here (the platform has a Holiday entity per tenant; folding it in is deferred to US-NTF when
    /// real notification timing matters). Returns the UTC instant at which the window expires.
    /// </summary>
    internal static DateTime AddBusinessDays(DateTime startUtc, int businessDays)
    {
        var result = startUtc;
        var added = 0;
        while (added < businessDays)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                added++;
        }
        return result;
    }
}
