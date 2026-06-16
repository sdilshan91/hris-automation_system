using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Log-only seam for performance notifications (US-PRF-001 AC-2/FR-7). Emits structured Serilog events
/// instead of real in-app/email delivery — the goal-setting flow is complete and observable without a
/// notification platform. Mirrors <c>LogOnlyRecruitmentNotificationService</c> /
/// <c>LogOnlyLeaveNotificationService</c>. TODO(US-NTF): replace with a real impl.
/// </summary>
public sealed class LogOnlyPerformanceNotificationService : IPerformanceNotificationService
{
    private readonly ILogger<LogOnlyPerformanceNotificationService> _logger;

    public LogOnlyPerformanceNotificationService(ILogger<LogOnlyPerformanceNotificationService> logger)
        => _logger = logger;

    public Task NotifyGoalChangedAsync(
        string eventType, Guid goalId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Performance notification SEAM] {EventType}: goal {GoalId} for employee {EmployeeId} in cycle {CycleId}. " +
            "Real in-app/email delivery deferred (US-NTF).",
            eventType, goalId, employeeId, cycleId);
        return Task.CompletedTask;
    }

    public Task NotifySelfAssessmentSubmittedAsync(
        Guid selfAssessmentId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Performance notification SEAM] self-assessment-submitted: {SelfAssessmentId} by employee {EmployeeId} " +
            "in cycle {CycleId}; manager notified. Real in-app/email delivery deferred (US-NTF).",
            selfAssessmentId, employeeId, cycleId);
        return Task.CompletedTask;
    }

    public Task NotifySelfAssessmentReminderAsync(
        Guid employeeId, Guid cycleId, int daysUntilDeadline, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Performance notification SEAM] self-assessment-reminder: employee {EmployeeId} in cycle {CycleId} " +
            "has {Days} day(s) until the self-assessment deadline. Real in-app/email delivery deferred (US-NTF).",
            employeeId, cycleId, daysUntilDeadline);
        return Task.CompletedTask;
    }

    public Task NotifyManagerReviewSubmittedAsync(
        Guid managerReviewId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Performance notification SEAM] manager-review-submitted: {ManagerReviewId} for employee {EmployeeId} " +
            "in cycle {CycleId}; employee notified. Real in-app/email delivery deferred (US-NTF).",
            managerReviewId, employeeId, cycleId);
        return Task.CompletedTask;
    }
}
