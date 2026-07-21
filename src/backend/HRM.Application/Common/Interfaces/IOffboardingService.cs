using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>Input for initiating an offboarding (US-ONB-005 AC-1, FR-2, §7).</summary>
public sealed record InitiateOffboardingInput(
    Guid EmployeeId,
    DateOnly LastWorkingDay,
    Guid? OffboardingTemplateId,
    Domain.Enums.OffboardingReason Reason,
    string? Notes);

/// <summary>Input for recording a department clearance decision (US-ONB-005 AC-3, §7).</summary>
public sealed record RecordClearanceInput(
    Guid TaskId,
    Domain.Enums.ClearanceStatus Status,
    string? Remarks);

/// <summary>Input for returning an asset against an offboarding task (US-ONB-005 AC-2, BR-3).</summary>
public sealed record ReturnAssetInput(
    Guid TaskId,
    Guid AssetId,
    Domain.Enums.AssetCondition Condition,
    bool Disposed);

/// <summary>
/// Offboarding / exit-clearance service (US-ONB-005). All operations are tenant-scoped via ITenantContext +
/// the EF global query filter (NFR-2/AC-6); the tenant_id is stamped from the session, never user input
/// (FR-8). Account deactivation reuses the existing user-active mechanism + refresh-token revocation; session
/// revocation uses the <see cref="ISessionRevoker"/> seam; F&amp;F uses the <see cref="IPayrollFnFIntegration"/>
/// seam. Audit columns (FR-9) are stamped by AuditInterceptor.
/// </summary>
public interface IOffboardingService
{
    /// <summary>AC-1/FR-2: initiate offboarding — creates the instance + exit/clearance tasks with LWD-relative due dates.</summary>
    Task<Result<OffboardingInstanceDto>> InitiateAsync(
        InitiateOffboardingInput input, CancellationToken cancellationToken = default);

    /// <summary>AC-3/FR-4: get a single offboarding instance with the clearance dashboard data.</summary>
    Task<Result<OffboardingInstanceDto>> GetByIdAsync(
        Guid offboardingInstanceId, CancellationToken cancellationToken = default);

    /// <summary>Gets the employee's current offboarding instance (or 404).</summary>
    Task<Result<OffboardingInstanceDto>> GetByEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>AC-3: record a department clearance decision (approved / pending_issues) and recompute status.</summary>
    Task<Result<OffboardingInstanceDto>> RecordClearanceAsync(
        RecordClearanceInput input, CancellationToken cancellationToken = default);

    /// <summary>AC-2/BR-3: return an asset — flip the register to Available (or Disposed) and complete the task.</summary>
    Task<Result<OffboardingInstanceDto>> ReturnAssetAsync(
        ReturnAssetInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// AC-4/AC-5/FR-5/FR-6/FR-7: complete the offboarding. Validates all mandatory tasks + clearances; when a
    /// mandatory item is pending the result's <c>Completed</c> is false and <c>PendingItems</c> is populated
    /// (the controller returns 409). On success: terminates the employee, deactivates the user account, revokes
    /// sessions, triggers F&amp;F to Payroll. Irreversible (BR-6).
    /// </summary>
    Task<Result<CompleteOffboardingResultDto>> CompleteAsync(
        Guid offboardingInstanceId, CancellationToken cancellationToken = default);
}
