using HRM.Application.Common.Models;
using HRM.Application.Features.OrganizationTree.DTOs;
using MediatR;

namespace HRM.Application.Features.OrganizationTree.Queries;

/// <summary>
/// Query for the organization tree (US-CHR-006).
/// Supports department hierarchy view and reporting structure view.
/// Lazy loading via parentId + depth parameters.
/// </summary>
public sealed record GetOrgTreeQuery(
    /// <summary>
    /// "department" (default) or "reporting".
    /// </summary>
    string View = "department",

    /// <summary>
    /// If set, return only children of this parent node (lazy loading).
    /// Null returns the full tree from root.
    /// </summary>
    Guid? ParentId = null,

    /// <summary>
    /// Maximum depth of children to return. Default 2. Max 10.
    /// </summary>
    int Depth = 2,

    /// <summary>
    /// When true, include inactive departments/employees (BR-4 toggle).
    /// </summary>
    bool IncludeInactive = false
) : IRequest<Result<OrgTreeResult>>;
