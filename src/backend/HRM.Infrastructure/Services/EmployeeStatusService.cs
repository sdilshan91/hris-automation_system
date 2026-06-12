using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Employee status management service (US-CHR-009).
/// Implements state machine, idempotency, future-dated changes, side effects,
/// employment history recording, and audit logging.
/// </summary>
public sealed class EmployeeStatusService : IEmployeeStatusService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<EmployeeStatusService> _logger;

    private const string OperationName = "ChangeEmployeeStatus";

    public EmployeeStatusService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<EmployeeStatusService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<ChangeEmployeeStatusResult>> ChangeStatusAsync(
        Guid employeeId,
        ChangeEmployeeStatusRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ChangeEmployeeStatusResult>.Failure("Tenant context is not resolved.", 400);

        // Idempotency check (NFR-3): if we already processed this key, return the cached response
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _dbContext.IdempotencyRecords
                .FirstOrDefaultAsync(r =>
                    r.IdempotencyKey == idempotencyKey &&
                    r.OperationName == OperationName,
                    cancellationToken);

            if (existing is not null)
            {
                if (existing.ResponseStatusCode >= 200 && existing.ResponseStatusCode < 300 && existing.ResponseJson is not null)
                {
                    var cachedResult = JsonSerializer.Deserialize<ChangeEmployeeStatusResult>(existing.ResponseJson);
                    _logger.LogInformation(
                        "Idempotent request detected. Key={Key}, EmployeeId={EmployeeId}, TenantId={TenantId}",
                        idempotencyKey, employeeId, _tenantContext.TenantId);
                    return Result<ChangeEmployeeStatusResult>.Success(cachedResult!);
                }

                // Previous attempt failed — let it retry
            }
        }

        // Load employee
        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        if (employee is null)
            return Result<ChangeEmployeeStatusResult>.Failure("Employee not found.", 404);

        // Validate the transition (FR-2, BR-1)
        if (!EmployeeStatusStateMachine.IsValidTransition(employee.Status, request.NewStatus))
        {
            return Result<ChangeEmployeeStatusResult>.Failure(
                EmployeeStatusStateMachine.GetInvalidTransitionMessage(employee.Status, request.NewStatus),
                400);
        }

        var previousStatus = employee.Status;
        var changedBy = _currentUser.IsAuthenticated ? _currentUser.Email : "system";
        var isFutureDated = request.EffectiveDate.Date > DateTime.UtcNow.Date;

        if (isFutureDated)
        {
            // BR-4: Store as future-dated change, do NOT apply now
            var pendingChange = new FutureDatedStatusChange
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                EmployeeId = employeeId,
                FromStatus = previousStatus,
                ToStatus = request.NewStatus,
                Reason = request.Reason,
                EffectiveDate = request.EffectiveDate.Date,
                RequestedByUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : Guid.Empty,
                RequestedBy = changedBy,
            };

            _dbContext.FutureDatedStatusChanges.Add(pendingChange);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var futureResult = new ChangeEmployeeStatusResult
            {
                EmployeeId = employeeId,
                PreviousStatus = previousStatus.ToString(),
                NewStatus = request.NewStatus.ToString(),
                EffectiveDate = request.EffectiveDate.Date,
                IsFutureDated = true,
                Message = $"Status change to {request.NewStatus} scheduled for {request.EffectiveDate:yyyy-MM-dd}. " +
                          "It will be applied automatically on the effective date.",
            };

            // Save idempotency record for future-dated changes too
            await SaveIdempotencyRecordAsync(idempotencyKey, futureResult, 200, cancellationToken);

            _logger.LogInformation(
                "Future-dated status change scheduled. EmployeeId={EmployeeId}, From={From}, To={To}, " +
                "EffectiveDate={EffectiveDate}, TenantId={TenantId}, By={User}",
                employeeId, previousStatus, request.NewStatus, request.EffectiveDate,
                _tenantContext.TenantId, changedBy);

            return Result<ChangeEmployeeStatusResult>.Success(futureResult);
        }

        // Apply immediately
        return await ApplyStatusChangeAsync(
            employee, previousStatus, request.NewStatus, request.Reason,
            request.EffectiveDate.Date, changedBy, idempotencyKey, cancellationToken);
    }

    /// <summary>
    /// Core logic to apply a status change, write employment history, audit log,
    /// trigger side effects, and save idempotency record.
    /// Shared by immediate changes and the future-dated job.
    /// </summary>
    internal async Task<Result<ChangeEmployeeStatusResult>> ApplyStatusChangeAsync(
        Employee employee,
        EmployeeStatus previousStatus,
        EmployeeStatus newStatus,
        string reason,
        DateTime effectiveDate,
        string changedBy,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        employee.Status = newStatus;

        // Update IsActive based on status (FR-5)
        employee.IsActive = newStatus is EmployeeStatus.Active or EmployeeStatus.Probation;

        // Record in EmploymentHistory (FR-4)
        _dbContext.EmploymentHistories.Add(new EmploymentHistory
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = employee.TenantId,
            EmployeeId = employee.Id,
            ChangeType = "status_change",
            PreviousValue = previousStatus.ToString(),
            NewValue = newStatus.ToString(),
            EffectiveDate = effectiveDate,
            Reason = reason,
            ChangedBy = changedBy,
        });

        // Write audit log entry with before/after (NFR-5)
        _dbContext.EmployeeFieldAuditLogs.Add(new EmployeeFieldAuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = employee.TenantId,
            EmployeeId = employee.Id,
            Section = "StatusChange",
            BeforeSnapshot = JsonSerializer.Serialize(new { Status = previousStatus.ToString(), IsActive = previousStatus is EmployeeStatus.Active or EmployeeStatus.Probation }),
            AfterSnapshot = JsonSerializer.Serialize(new { Status = newStatus.ToString(), employee.IsActive }),
            ChangedByUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            ChangedBy = changedBy,
        });

        // Side effects (FR-5, AC-3)
        await ApplySideEffectsAsync(employee, newStatus, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var result = new ChangeEmployeeStatusResult
        {
            EmployeeId = employee.Id,
            PreviousStatus = previousStatus.ToString(),
            NewStatus = newStatus.ToString(),
            EffectiveDate = effectiveDate,
            IsFutureDated = false,
            Message = $"Employee status changed from {previousStatus} to {newStatus}.",
        };

        await SaveIdempotencyRecordAsync(idempotencyKey, result, 200, cancellationToken);

        _logger.LogInformation(
            "Employee status changed. EmployeeId={EmployeeId}, From={From}, To={To}, " +
            "EffectiveDate={EffectiveDate}, TenantId={TenantId}, By={User}",
            employee.Id, previousStatus, newStatus, effectiveDate,
            employee.TenantId, changedBy);

        return Result<ChangeEmployeeStatusResult>.Success(result);
    }

    public async Task<Result<ValidTransitionsResult>> GetValidTransitionsAsync(
        Guid employeeId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ValidTransitionsResult>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        if (employee is null)
            return Result<ValidTransitionsResult>.Failure("Employee not found.", 404);

        var transitions = EmployeeStatusStateMachine.GetValidTransitions(employee.Status);

        return Result<ValidTransitionsResult>.Success(new ValidTransitionsResult
        {
            CurrentStatus = employee.Status.ToString(),
            ValidTransitions = transitions.Select(t => t.ToString()).ToList(),
        });
    }

    public async Task ApplyPendingFutureDatedChangesAsync(CancellationToken cancellationToken = default)
    {
        var today = DateTime.UtcNow.Date;

        // Query across all tenants (background job context; use IgnoreQueryFilters)
        var pendingChanges = await _dbContext.FutureDatedStatusChanges
            .IgnoreQueryFilters()
            .Where(f => !f.IsDeleted && !f.IsApplied && !f.IsCancelled && f.EffectiveDate <= today)
            .Include(f => f.Employee)
            .ToListAsync(cancellationToken);

        foreach (var pending in pendingChanges)
        {
            if (pending.Employee is null)
            {
                _logger.LogWarning(
                    "Future-dated status change {Id} references non-existent employee {EmployeeId}. Marking cancelled.",
                    pending.Id, pending.EmployeeId);
                pending.IsCancelled = true;
                continue;
            }

            // Validate the transition is still valid (the employee's current status may have changed since scheduling)
            if (!EmployeeStatusStateMachine.IsValidTransition(pending.Employee.Status, pending.ToStatus))
            {
                _logger.LogWarning(
                    "Future-dated status change {Id} is no longer valid: current status is {CurrentStatus}, " +
                    "scheduled transition was {From} -> {To}. Marking cancelled.",
                    pending.Id, pending.Employee.Status, pending.FromStatus, pending.ToStatus);
                pending.IsCancelled = true;
                continue;
            }

            var previousStatus = pending.Employee.Status;
            pending.Employee.Status = pending.ToStatus;
            pending.Employee.IsActive = pending.ToStatus is EmployeeStatus.Active or EmployeeStatus.Probation;

            // Record in EmploymentHistory
            _dbContext.EmploymentHistories.Add(new EmploymentHistory
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = pending.TenantId,
                EmployeeId = pending.EmployeeId,
                ChangeType = "status_change",
                PreviousValue = previousStatus.ToString(),
                NewValue = pending.ToStatus.ToString(),
                EffectiveDate = pending.EffectiveDate,
                Reason = pending.Reason,
                ChangedBy = $"{pending.RequestedBy} (scheduled)",
            });

            // Write audit log
            _dbContext.EmployeeFieldAuditLogs.Add(new EmployeeFieldAuditLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = pending.TenantId,
                EmployeeId = pending.EmployeeId,
                Section = "StatusChange",
                BeforeSnapshot = JsonSerializer.Serialize(new { Status = previousStatus.ToString() }),
                AfterSnapshot = JsonSerializer.Serialize(new { Status = pending.ToStatus.ToString() }),
                ChangedBy = $"{pending.RequestedBy} (scheduled job)",
            });

            // Side effects
            await ApplySideEffectsForBackgroundJobAsync(pending.Employee, pending.ToStatus, pending.TenantId, cancellationToken);

            pending.IsApplied = true;
            pending.AppliedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Applied future-dated status change. Id={ChangeId}, EmployeeId={EmployeeId}, " +
                "From={From}, To={To}, TenantId={TenantId}",
                pending.Id, pending.EmployeeId, previousStatus, pending.ToStatus, pending.TenantId);
        }

        if (pendingChanges.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Processed {Count} future-dated status changes.", pendingChanges.Count);
        }
    }

    public async Task CheckProbationEndDatesAsync(CancellationToken cancellationToken = default)
    {
        // BR-6: Default probation period is 90 days from date_of_joining
        // Check if probation end is within 7 days
        var today = DateTime.UtcNow.Date;
        var reminderThreshold = today.AddDays(7);

        // Query across all tenants (background job context)
        var probationEmployees = await _dbContext.Employees
            .IgnoreQueryFilters()
            .Where(e => !e.IsDeleted &&
                        e.Status == EmployeeStatus.Probation &&
                        e.DateOfJoining.AddDays(90) >= today &&
                        e.DateOfJoining.AddDays(90) <= reminderThreshold)
            .Select(e => new
            {
                e.Id,
                e.EmployeeNo,
                e.FirstName,
                e.LastName,
                e.Email,
                e.DateOfJoining,
                ProbationEndDate = e.DateOfJoining.AddDays(90),
                e.TenantId,
            })
            .ToListAsync(cancellationToken);

        foreach (var emp in probationEmployees)
        {
            var daysUntilEnd = (emp.ProbationEndDate - today).Days;

            // TODO(notification): Dispatch an actual HR notification when the Notification module is built.
            // For now, log the reminder as structured data so it can be picked up by log-based alerts.
            _logger.LogWarning(
                "PROBATION_REMINDER: Employee {EmployeeNo} ({FirstName} {LastName}) in tenant {TenantId} " +
                "has probation ending on {ProbationEndDate} ({DaysRemaining} days remaining). " +
                "HR should confirm transition to Active or extend probation.",
                emp.EmployeeNo, emp.FirstName, emp.LastName, emp.TenantId,
                emp.ProbationEndDate, daysUntilEnd);
        }

        if (probationEmployees.Count > 0)
        {
            _logger.LogInformation(
                "Probation reminder check complete. Found {Count} employees approaching probation end.",
                probationEmployees.Count);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Applies side effects for status changes (FR-5, AC-3).
    /// CROSS-CUTTING CHANGE: disables/enables user login by setting User.IsActive.
    /// </summary>
    private async Task ApplySideEffectsAsync(
        Employee employee,
        EmployeeStatus newStatus,
        CancellationToken cancellationToken)
    {
        switch (newStatus)
        {
            case EmployeeStatus.Terminated:
            case EmployeeStatus.Suspended:
                // Disable linked user login (AC-3, FR-5)
                await DisableUserLoginAsync(employee, cancellationToken);
                // US-CHR-011 BR-4: If a manager is terminated/suspended, notify HR to reassign direct reports
                await NotifyDirectReportsReassignmentAsync(employee, newStatus, cancellationToken);
                // TODO(leave): Pause leave accrual when Leave module is built
                // TODO(payroll): Exclude from payroll when Payroll module is built
                // TODO(onboarding): Trigger offboarding workflow when Onboarding module is built
                break;

            case EmployeeStatus.Active:
                // Re-enable linked user login
                await EnableUserLoginAsync(employee, cancellationToken);
                // TODO(leave): Resume leave accrual when Leave module is built
                break;
        }
    }

    /// <summary>
    /// Side effects for the background job context (no ICurrentUser available).
    /// </summary>
    private async Task ApplySideEffectsForBackgroundJobAsync(
        Employee employee,
        EmployeeStatus newStatus,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        switch (newStatus)
        {
            case EmployeeStatus.Terminated:
            case EmployeeStatus.Suspended:
                await DisableUserLoginAsync(employee, cancellationToken);
                await NotifyDirectReportsReassignmentAsync(employee, newStatus, cancellationToken);
                break;

            case EmployeeStatus.Active:
                await EnableUserLoginAsync(employee, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// US-CHR-011 BR-4: When a manager is terminated or suspended, log an HR reminder
    /// to reassign their direct reports. Does NOT auto-reassign.
    /// TODO(notification): Dispatch an actual notification when the Notification module is built.
    /// </summary>
    private async Task NotifyDirectReportsReassignmentAsync(
        Employee employee,
        EmployeeStatus newStatus,
        CancellationToken cancellationToken)
    {
        var directReportCount = await _dbContext.Employees
            .CountAsync(e => e.ReportsToEmployeeId == employee.Id, cancellationToken);

        if (directReportCount == 0)
            return;

        // TODO(notification): Replace with actual notification dispatch when Notification module is built.
        _logger.LogWarning(
            "MANAGER_STATUS_CHANGE_REASSIGNMENT_NEEDED: Manager {EmployeeId} ({FirstName} {LastName}) " +
            "has been {NewStatus}. They have {DirectReportCount} direct report(s) that need to be reassigned. " +
            "TenantId={TenantId}.",
            employee.Id, employee.FirstName, employee.LastName, newStatus,
            directReportCount, employee.TenantId);
    }

    /// <summary>
    /// CROSS-CUTTING AUTH CHANGE: Disables the linked user account and revokes active refresh tokens.
    /// This prevents the employee from logging in after termination/suspension.
    /// Touches the User entity (auth module) -- flagged as a cross-cutting change per US-CHR-009 AC-3.
    /// </summary>
    private async Task DisableUserLoginAsync(Employee employee, CancellationToken cancellationToken)
    {
        if (employee.UserId is null)
            return;

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == employee.UserId.Value, cancellationToken);

        if (user is null)
            return;

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;

        // Revoke all active refresh tokens for this user
        var activeTokens = await _dbContext.RefreshTokens
            .IgnoreQueryFilters()
            .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTime.UtcNow;
        }

        _logger.LogInformation(
            "Disabled user login. UserId={UserId}, EmployeeId={EmployeeId}, RevokedTokens={TokenCount}",
            user.Id, employee.Id, activeTokens.Count);
    }

    /// <summary>
    /// Re-enables the linked user account on reactivation.
    /// </summary>
    private async Task EnableUserLoginAsync(Employee employee, CancellationToken cancellationToken)
    {
        if (employee.UserId is null)
            return;

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == employee.UserId.Value, cancellationToken);

        if (user is null)
            return;

        user.IsActive = true;
        user.UpdatedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Re-enabled user login. UserId={UserId}, EmployeeId={EmployeeId}",
            user.Id, employee.Id);
    }

    private async Task SaveIdempotencyRecordAsync(
        string? idempotencyKey,
        ChangeEmployeeStatusResult result,
        int statusCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return;

        // Check if record already exists (could have been created by a concurrent request)
        var exists = await _dbContext.IdempotencyRecords
            .AnyAsync(r =>
                r.IdempotencyKey == idempotencyKey &&
                r.OperationName == OperationName,
                cancellationToken);

        if (exists)
            return;

        _dbContext.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.IsResolved ? _tenantContext.TenantId : Guid.Empty,
            IdempotencyKey = idempotencyKey,
            OperationName = OperationName,
            ResponseJson = JsonSerializer.Serialize(result),
            ResponseStatusCode = statusCode,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
        });

        // Save separately to avoid transaction issues if the caller already saved
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Race condition: another request already saved the same key. This is fine.
            _logger.LogDebug(
                "Idempotency record already exists for key {Key}. Ignoring duplicate insert.", idempotencyKey);
        }
    }
}
