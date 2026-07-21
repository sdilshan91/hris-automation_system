using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-ONB-004: lite asset-register service for issuance tracking during onboarding. All queries are
/// tenant-scoped via ITenantContext + the EF global query filter (NFR-2/AC-5); the tenant_id is stamped
/// from the session, never user input (FR-7). Issuance is atomic (NFR-5).
/// </summary>
public interface IAssetService
{
    /// <summary>FR-3: lists Available assets (optionally filtered by type) for the issuance dropdown.</summary>
    Task<Result<IReadOnlyList<AvailableAssetDto>>> GetAvailableAsync(
        string? assetType, CancellationToken cancellationToken = default);

    /// <summary>AC-4 (HR view): lists the assets currently assigned to a given employee.</summary>
    Task<Result<IReadOnlyList<AssetDto>>> GetByEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>AC-4/BR-6: lists the logged-in employee's own assigned assets (read-only).</summary>
    Task<Result<IReadOnlyList<AssetDto>>> GetMineAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// FR-5: bulk-issues one or more assets to an employee in a SINGLE transaction (NFR-5). Enforces the
    /// available-status gate (BR-1/FR-3), the double-assignment guard (AC-3), and per-tenant tag/serial
    /// uniqueness (BR-3); optionally attaches the acknowledgment doc (FR-6) and completes the linked
    /// onboarding task (AC-2).
    /// </summary>
    Task<Result<IReadOnlyList<AssetDto>>> IssueAsync(
        IssueAssetsInput input, CancellationToken cancellationToken = default);
}

/// <summary>Service input for a single issued asset line (FR-5).</summary>
public sealed record IssueAssetLineInput(
    Guid? AssetId,
    string AssetType,
    string AssetTag,
    string? SerialNumber,
    string? Brand,
    string? Model,
    AssetCondition Condition,
    DateOnly IssueDate,
    string? Notes);

/// <summary>
/// Service input for bulk asset issuance (FR-5/FR-6). The acknowledgment attachment stream (when supplied)
/// is owned by the caller. When <see cref="TaskInstanceId"/> is set, that onboarding task is completed in
/// the same transaction (AC-2).
/// </summary>
public sealed record IssueAssetsInput(
    Guid EmployeeId,
    Guid? TaskInstanceId,
    IReadOnlyList<IssueAssetLineInput> Assets,
    Stream? AcknowledgmentStream,
    string? AcknowledgmentFileName,
    string? AcknowledgmentContentType,
    long AcknowledgmentSize);
