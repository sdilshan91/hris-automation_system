using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Log-only seam for attendance-family notifications (US-NTF-006 Phase 6: US-ATT-001/004/006). Emits structured
/// Serilog events instead of real in-app/email delivery — so the attendance flow is complete and observable
/// without the notification platform. Mirrors <c>LogOnlyPerformanceNotificationService</c>. Retained so
/// integration tests can register it in place of <c>RealAttendanceNotificationService</c>.
/// </summary>
public sealed class LogOnlyAttendanceNotificationService : IAttendanceNotificationService
{
    private readonly ILogger<LogOnlyAttendanceNotificationService> _logger;

    public LogOnlyAttendanceNotificationService(ILogger<LogOnlyAttendanceNotificationService> logger)
        => _logger = logger;

    public Task NotifyLateAsync(
        Guid employeeId, DateOnly date, TimeOnly checkIn, TimeOnly? expectedStart,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Attendance notification SEAM] attendance-late: employee {EmployeeId} clocked in at {CheckIn} on " +
            "{Date} (expected {Expected}). Real in-app/email delivery deferred (US-NTF).",
            employeeId, checkIn, date, expectedStart);
        return Task.CompletedTask;
    }

    public Task NotifyRegularizationRequestedAsync(
        Guid employeeId, DateOnly date, string reason, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Attendance notification SEAM] regularization-requested: employee {EmployeeId} for {Date}; line " +
            "manager notified. Real in-app/email delivery deferred (US-NTF).",
            employeeId, date);
        return Task.CompletedTask;
    }

    public Task NotifyRegularizationDecidedAsync(
        bool approved, Guid employeeId, DateOnly date, string reason, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Attendance notification SEAM] regularization-{Decision}: employee {EmployeeId} for {Date}. " +
            "Real in-app/email delivery deferred (US-NTF).",
            approved ? "approved" : "rejected", employeeId, date);
        return Task.CompletedTask;
    }

    public Task NotifyOvertimeMaximaExceededAsync(
        Guid employeeId, decimal hours, decimal limit, string period, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Attendance notification SEAM] overtime-maxima-exceeded: employee {EmployeeId} recorded {Hours}h vs " +
            "{Period} limit {Limit}h; attendance-admin pool notified. Real in-app/email delivery deferred (US-NTF).",
            employeeId, hours, period, limit);
        return Task.CompletedTask;
    }

    public Task NotifyOvertimePreApprovalRequestedAsync(
        Guid employeeId, DateOnly date, decimal hours, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Attendance notification SEAM] overtime-preapproval-requested: employee {EmployeeId} for {Hours}h on " +
            "{Date}; manager notified. Real in-app/email delivery deferred (US-NTF).",
            employeeId, hours, date);
        return Task.CompletedTask;
    }

    public Task NotifyOvertimeDecidedAsync(
        bool approved, Guid employeeId, DateOnly date, decimal hours, string? reason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Attendance notification SEAM] overtime-{Decision}: employee {EmployeeId} for {Hours}h on {Date}. " +
            "Real in-app/email delivery deferred (US-NTF).",
            approved ? "approved" : "rejected", employeeId, hours, date);
        return Task.CompletedTask;
    }
}
