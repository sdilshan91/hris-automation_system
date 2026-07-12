using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Log-only seam for Core-HR notifications (US-NTF-006 Phase 7: US-CHR-008/009/011). Emits structured Serilog
/// events instead of real in-app/email delivery — so the Core-HR flows are complete and observable without the
/// notification platform. Mirrors <c>LogOnlyAttendanceNotificationService</c>. Retained so tests can register it
/// in place of <c>RealCoreHrNotificationService</c>.
/// </summary>
public sealed class LogOnlyCoreHrNotificationService : ICoreHrNotificationService
{
    private readonly ILogger<LogOnlyCoreHrNotificationService> _logger;

    public LogOnlyCoreHrNotificationService(ILogger<LogOnlyCoreHrNotificationService> logger)
        => _logger = logger;

    public Task NotifyProbationEndingAsync(
        Guid tenantId, Guid employeeId, DateOnly probationEndDate, int daysRemaining,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Core-HR notification SEAM] probation-ending: employee {EmployeeId} in tenant {TenantId} ends " +
            "probation on {ProbationEndDate} ({DaysRemaining} day(s) remaining); HR pool notified. Real in-app/" +
            "email delivery deferred (US-NTF).",
            employeeId, tenantId, probationEndDate, daysRemaining);
        return Task.CompletedTask;
    }

    public Task NotifyManagerReassignmentNeededAsync(
        Guid managerEmployeeId, EmployeeStatus newStatus, int directReportCount,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Core-HR notification SEAM] manager-reassignment-needed: manager {ManagerId} is now {NewStatus} with " +
            "{DirectReportCount} direct report(s); HR pool notified. Real in-app/email delivery deferred (US-NTF).",
            managerEmployeeId, newStatus, directReportCount);
        return Task.CompletedTask;
    }

    public Task NotifyDocumentExpiryAsync(
        Guid tenantId, Guid employeeId, string fileName, string category, DateOnly expiryDate, int daysUntilExpiry,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Core-HR notification SEAM] document-expiry: document \"{FileName}\" ({Category}) for employee " +
            "{EmployeeId} in tenant {TenantId} expires on {ExpiryDate} ({DaysUntilExpiry} day(s) remaining); owner " +
            "notified. Real in-app/email delivery deferred (US-NTF).",
            fileName, category, employeeId, tenantId, expiryDate, daysUntilExpiry);
        return Task.CompletedTask;
    }
}
