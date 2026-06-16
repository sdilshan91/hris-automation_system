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

    public Task NotifyCycleEventAsync(
        string eventType, Guid cycleId, Guid employeeId, string? detail = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Performance notification SEAM] {EventType}: cycle {CycleId}, participant {EmployeeId}{Detail}. " +
            "Real in-app/email delivery deferred (US-NTF).",
            eventType, cycleId, employeeId, detail is null ? string.Empty : $" ({detail})");
        return Task.CompletedTask;
    }

    public Task NotifyReviewerAssignedAsync(
        Guid reviewerEmployeeId, Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Performance notification SEAM] 360-reviewer-assigned: reviewer {ReviewerId} has a pending 360 " +
            "feedback form for reviewee {RevieweeId} in cycle {CycleId}. Real in-app/email delivery deferred (US-NTF).",
            reviewerEmployeeId, revieweeEmployeeId, cycleId);
        return Task.CompletedTask;
    }

    public Task NotifyReviewerReminderAsync(
        Guid reviewerEmployeeId, Guid revieweeEmployeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Performance notification SEAM] 360-reviewer-reminder: reviewer {ReviewerId} has not submitted 360 " +
            "feedback for reviewee {RevieweeId} in cycle {CycleId}. Real in-app/email delivery deferred (US-NTF).",
            reviewerEmployeeId, revieweeEmployeeId, cycleId);
        return Task.CompletedTask;
    }

    public Task NotifyReviewSignOffRequestedAsync(
        Guid managerReviewId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Performance notification SEAM] review-signoff-requested: review {ManagerReviewId} for employee " +
            "{EmployeeId} in cycle {CycleId}; employee asked to acknowledge & sign. Real in-app/email delivery deferred (US-NTF).",
            managerReviewId, employeeId, cycleId);
        return Task.CompletedTask;
    }

    public Task NotifyReviewDisputedAsync(
        Guid managerReviewId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Performance notification SEAM] review-disputed: review {ManagerReviewId} for employee {EmployeeId} " +
            "in cycle {CycleId}; manager + HR notified for resolution. Real in-app/email delivery deferred (US-NTF).",
            managerReviewId, employeeId, cycleId);
        return Task.CompletedTask;
    }

    public Task NotifyReviewAutoClosedAsync(
        Guid managerReviewId, Guid employeeId, Guid cycleId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Performance notification SEAM] review-auto-closed: review {ManagerReviewId} for employee {EmployeeId} " +
            "in cycle {CycleId} auto-closed (No Response); HR notified. Real in-app/email delivery deferred (US-NTF).",
            managerReviewId, employeeId, cycleId);
        return Task.CompletedTask;
    }

    public Task NotifyPipEventAsync(
        string eventType, Guid pipId, Guid employeeId, Guid? recipientEmployeeId, string? detail = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Performance notification SEAM] {EventType}: PIP {PipId} for employee {EmployeeId}, recipient " +
            "{Recipient}{Detail}. Real in-app/email delivery deferred (US-NTF).",
            eventType, pipId, employeeId, recipientEmployeeId, detail is null ? string.Empty : $" ({detail})");
        return Task.CompletedTask;
    }
}
