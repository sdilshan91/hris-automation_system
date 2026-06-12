using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for employee status management (US-CHR-009).
/// Handles status transitions, future-dated changes, idempotency, and side effects.
/// </summary>
public interface IEmployeeStatusService
{
    /// <summary>
    /// Changes an employee's status with state machine validation, side effects,
    /// employment history recording, and audit logging.
    /// If effectiveDate is in the future, stores the change without applying it.
    /// </summary>
    Task<Result<ChangeEmployeeStatusResult>> ChangeStatusAsync(
        Guid employeeId,
        ChangeEmployeeStatusRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the valid transitions from the employee's current status.
    /// </summary>
    Task<Result<ValidTransitionsResult>> GetValidTransitionsAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies all pending future-dated status changes that are due (effectiveDate <= today).
    /// Called by the daily Hangfire job.
    /// </summary>
    Task ApplyPendingFutureDatedChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for employees on probation whose end date is within 7 days
    /// and logs HR reminders. Called by the daily Hangfire job.
    /// </summary>
    Task CheckProbationEndDatesAsync(CancellationToken cancellationToken = default);
}
