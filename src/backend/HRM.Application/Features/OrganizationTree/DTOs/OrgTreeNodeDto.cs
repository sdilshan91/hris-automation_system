namespace HRM.Application.Features.OrganizationTree.DTOs;

/// <summary>
/// Unified node DTO for the organization tree (US-CHR-006).
/// Represents either a department or an employee node.
/// </summary>
public sealed record OrgTreeNodeDto
{
    /// <summary>
    /// Department ID or Employee ID.
    /// </summary>
    public Guid NodeId { get; init; }

    /// <summary>
    /// "department" or "employee".
    /// </summary>
    public string NodeType { get; init; } = string.Empty;

    /// <summary>
    /// Department name or employee full name.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Job title for employee nodes; null for department nodes.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Profile photo URL for employee nodes; null for department nodes.
    /// </summary>
    public string? AvatarUrl { get; init; }

    /// <summary>
    /// Count of active employees in this department (department nodes only).
    /// </summary>
    public int? EmployeeCount { get; init; }

    /// <summary>
    /// Number of direct children (sub-departments + employee leaf nodes for dept view,
    /// direct reports for reporting view). Used by the client to show expand controls.
    /// </summary>
    public int ChildrenCount { get; init; }

    /// <summary>
    /// Parent department ID or parent manager ID. Null for root nodes.
    /// </summary>
    public Guid? ParentId { get; init; }

    /// <summary>
    /// Whether the node is active. Inactive nodes only appear when includeInactive=true.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Child nodes, populated up to the requested depth. Empty list when lazy-loading
    /// (children_count > 0 but children not yet fetched).
    /// </summary>
    public IReadOnlyList<OrgTreeNodeDto> Children { get; init; } = [];
}

/// <summary>
/// Response wrapper for the org tree endpoint.
/// </summary>
public sealed record OrgTreeResult
{
    /// <summary>
    /// Root-level nodes (top departments or top managers).
    /// </summary>
    public IReadOnlyList<OrgTreeNodeDto> Nodes { get; init; } = [];

    /// <summary>
    /// The view used to build this tree.
    /// </summary>
    public string View { get; init; } = "department";

    /// <summary>
    /// Whether the reporting view is fully implemented.
    /// False when the Employee.ReportsTo FK does not exist yet (deferred).
    /// </summary>
    public bool ReportingViewAvailable { get; init; }
}
