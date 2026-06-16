using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// Tenant-aware Hangfire job that drives one cycle's phase-transition notifications (US-PRF-004 FR-5/AC-2/
/// AC-4/NFR-3). For the given cycle it sets the tenant context (so the EF global query filters apply) and,
/// for each measurable phase (GoalSetting / SelfAssessment / ManagerReview), dispatches via the existing
/// <see cref="IPerformanceNotificationService"/> seam:
///   - phase-start: "now" within the first day of the phase window.
///   - deadline-reminder: the phase end is at one of the configured day thresholds (7/3/1).
///   - phase-close: the phase end has just passed.
///   - overdue-escalation: the phase end has passed and participants have not completed it.
/// Resilience is provided by Hangfire's automatic retry (NFR-3). Real in-app/email delivery is deferred to
/// US-NTF (the notification seam is log-only). Mirrors the tenant-context structure of
/// <see cref="SelfAssessmentReminderJob"/>; idempotent per run.
/// </summary>
public sealed class CyclePhaseTransitionJob
{
    private static readonly int[] ReminderThresholds = [7, 3, 1];

    private readonly IServiceScopeFactory _scopeFactory;

    public CyclePhaseTransitionJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RunAsync(Guid tenantId, Guid cycleId)
    {
        using var scope = _scopeFactory.CreateScope();

        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        if (tenantContext is Infrastructure.Services.TenantContext mutableContext)
            mutableContext.SetTenant(tenantId, $"tenant-{tenantId}", Domain.Entities.TenantStatus.Active);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<IPerformanceNotificationService>();

        var now = DateTime.UtcNow;
        var today = now.Date;

        var cycle = await db.AppraisalCycles
            .AsNoTracking()
            .Include(c => c.Phases)
            .FirstOrDefaultAsync(c => c.Id == cycleId);
        if (cycle is null || cycle.Status != AppraisalCycleStatus.Active)
        {
            Log.Information("CyclePhaseTransitionJob: cycle {CycleId} not found or not active; skipping", cycleId);
            return;
        }

        var participantIds = await db.CycleParticipants
            .AsNoTracking()
            .Where(p => p.CycleId == cycleId)
            .Select(p => p.EmployeeId)
            .ToListAsync();
        if (participantIds.Count == 0)
            return;

        foreach (var phase in cycle.Phases)
        {
            if (phase.PhaseType is not (CyclePhaseType.GoalSetting or CyclePhaseType.SelfAssessment or CyclePhaseType.ManagerReview))
                continue;

            var completed = await CompletedSetAsync(db, cycleId, phase.PhaseType);
            var pending = participantIds.Where(id => !completed.Contains(id)).ToList();

            if (phase.StartDate.Date == today)
                await DispatchAsync(notifications, "phase-start", cycleId, participantIds, phase.PhaseType.ToString());

            var daysToEnd = (phase.EndDate.Date - today).Days;
            if (ReminderThresholds.Contains(daysToEnd))
                await DispatchAsync(notifications, "deadline-reminder", cycleId, pending, $"{phase.PhaseType} closes in {daysToEnd}d");

            if (phase.EndDate.Date == today.AddDays(-1) || phase.EndDate.Date == today)
                await DispatchAsync(notifications, "phase-close", cycleId, participantIds, phase.PhaseType.ToString());

            if (phase.EndDate < now && pending.Count > 0)
                await DispatchAsync(notifications, "overdue-escalation", cycleId, pending, $"{phase.PhaseType} overdue");
        }

        Log.Information("CyclePhaseTransitionJob completed for cycle {CycleId}, tenant {TenantId}", cycleId, tenantId);
    }

    private static async Task<HashSet<Guid>> CompletedSetAsync(AppDbContext db, Guid cycleId, CyclePhaseType phaseType)
    {
        switch (phaseType)
        {
            case CyclePhaseType.GoalSetting:
                return (await db.Goals.AsNoTracking().Where(g => g.CycleId == cycleId)
                    .Select(g => g.EmployeeId).Distinct().ToListAsync()).ToHashSet();
            case CyclePhaseType.SelfAssessment:
                return (await db.SelfAssessments.AsNoTracking()
                    .Where(s => s.CycleId == cycleId && s.Status == SelfAssessmentStatus.Submitted)
                    .Select(s => s.EmployeeId).ToListAsync()).ToHashSet();
            case CyclePhaseType.ManagerReview:
                return (await db.ManagerReviews.AsNoTracking()
                    .Where(m => m.CycleId == cycleId && m.Status == ManagerReviewStatus.Submitted)
                    .Select(m => m.EmployeeId).ToListAsync()).ToHashSet();
            default:
                return [];
        }
    }

    private static async Task DispatchAsync(
        IPerformanceNotificationService notifications, string eventType, Guid cycleId,
        IReadOnlyList<Guid> employeeIds, string detail)
    {
        foreach (var employeeId in employeeIds)
            await notifications.NotifyCycleEventAsync(eventType, cycleId, employeeId, detail);
    }
}
