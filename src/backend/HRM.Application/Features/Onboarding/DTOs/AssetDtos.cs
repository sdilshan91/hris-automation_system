using HRM.Domain.Enums;

namespace HRM.Application.Features.Onboarding.DTOs;

/// <summary>
/// US-ONB-004 FR-3: an available asset shown in the issuance dropdown. Matches the pinned frontend contract
/// for GET /api/v1/onboarding/assets/available.
/// </summary>
public sealed record AvailableAssetDto
{
    public Guid AssetId { get; init; }
    public string AssetType { get; init; } = string.Empty;
    public string AssetTag { get; init; } = string.Empty;
    public string? SerialNumber { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string Condition { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// US-ONB-004 AC-2/AC-4/FR-4: a full asset register record (HR view + employee self-service view). Matches
/// the pinned contract for GET /api/v1/onboarding/assets, /assets/me, and the POST /assets/issue response.
/// </summary>
public sealed record AssetDto
{
    public Guid AssetId { get; init; }
    public string AssetType { get; init; } = string.Empty;
    public string AssetTag { get; init; } = string.Empty;
    public string? SerialNumber { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public DateTime? PurchaseDate { get; init; }
    public string Condition { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public Guid? AssignedEmployeeId { get; init; }
    public DateTime? IssueDate { get; init; }
    public string? Notes { get; init; }
    public string? AcknowledgmentUrl { get; init; }
}

/// <summary>One asset line in a bulk-issuance request (FR-5, §7). assetId is set when re-issuing an existing
/// register asset; null when entering an ad-hoc asset inline (the register row is created).</summary>
public sealed record IssueAssetLineDto
{
    public Guid? AssetId { get; init; }
    public string AssetType { get; init; } = string.Empty;
    public string AssetTag { get; init; } = string.Empty;
    public string? SerialNumber { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string Condition { get; init; } = string.Empty;
    public DateTime IssueDate { get; init; }
    public string? Notes { get; init; }
}

/// <summary>The JSON "request" field of the multipart bulk-issuance POST (FR-5). The optional acknowledgment
/// file is bound separately as the multipart "acknowledgment" field (FR-6).</summary>
public sealed record IssueAssetsRequestDto
{
    public Guid EmployeeId { get; init; }
    public Guid? TaskInstanceId { get; init; }
    public IReadOnlyList<IssueAssetLineDto> Assets { get; init; } = [];
}
