using HRM.Application.Common.Models;
using HRM.Application.Features.OrganizationTree.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Service interface for organization tree queries (US-CHR-006).
/// All operations are tenant-scoped via ITenantContext.
/// </summary>
public interface IOrganizationTreeService
{
    /// <summary>
    /// Returns the org tree as department hierarchy with employee counts.
    /// Supports lazy loading via parentId and depth control.
    /// </summary>
    Task<Result<OrgTreeResult>> GetDepartmentTreeAsync(
        Guid? parentId,
        int depth,
        bool includeInactive,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the org tree as a reporting structure (manager -> direct reports).
    /// NOTE: Deferred until Employee.ReportsTo FK exists (US-CHR-011).
    /// Current implementation returns department managers with their department employees.
    /// </summary>
    Task<Result<OrgTreeResult>> GetReportingTreeAsync(
        Guid? parentId,
        int depth,
        bool includeInactive,
        CancellationToken cancellationToken = default);
}
