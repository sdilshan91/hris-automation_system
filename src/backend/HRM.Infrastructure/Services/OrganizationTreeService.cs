using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.OrganizationTree.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Organization tree service (US-CHR-006).
/// Builds org tree from existing Department hierarchy and Employee data.
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// </summary>
public sealed class OrganizationTreeService : IOrganizationTreeService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<OrganizationTreeService> _logger;

    public OrganizationTreeService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<OrganizationTreeService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<OrgTreeResult>> GetDepartmentTreeAsync(
        Guid? parentId,
        int depth,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OrgTreeResult>.Failure("Tenant context is not resolved.", 400);

        // Load all departments for the tenant in one query.
        // For very large orgs this could be optimized to load only the needed subtree,
        // but for typical org sizes (<1000 departments) this is efficient (NFR-1).
        var deptQuery = _dbContext.Departments
            .Include(d => d.Manager)
            .AsNoTracking();

        if (!includeInactive)
            deptQuery = deptQuery.Where(d => d.IsActive);

        var allDepartments = await deptQuery
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        // Load employee counts per department in a single batch query.
        var empCountQuery = _dbContext.Employees.AsNoTracking();
        if (!includeInactive)
            empCountQuery = empCountQuery.Where(e => e.IsActive);

        var employeeCountsByDept = await empCountQuery
            .GroupBy(e => e.DepartmentId)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, cancellationToken);

        // Build lookup by parent ID for tree construction.
        var childrenLookup = allDepartments.ToLookup(d => d.ParentDepartmentId);

        // Determine the starting nodes based on parentId.
        IEnumerable<Department> rootDepartments;
        if (parentId.HasValue)
        {
            // Lazy load: return children of the specified parent.
            var parentExists = allDepartments.Any(d => d.Id == parentId.Value);
            if (!parentExists)
                return Result<OrgTreeResult>.Failure("Parent department not found.", 404);

            rootDepartments = childrenLookup[parentId.Value];
        }
        else
        {
            // Full tree: root departments (no parent).
            rootDepartments = childrenLookup[null];
        }

        var nodes = rootDepartments
            .Select(d => BuildDepartmentNode(d, childrenLookup, employeeCountsByDept, depth, 1))
            .ToList();

        return Result<OrgTreeResult>.Success(new OrgTreeResult
        {
            Nodes = nodes,
            View = "department",
            ReportingViewAvailable = false,
        });
    }

    /// <inheritdoc />
    public async Task<Result<OrgTreeResult>> GetReportingTreeAsync(
        Guid? parentId,
        int depth,
        bool includeInactive,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OrgTreeResult>.Failure("Tenant context is not resolved.", 400);

        // US-CHR-011: Real reporting structure using Employee.ReportsToEmployeeId.
        // Root nodes are employees with no manager (ReportsToEmployeeId == null).
        // Children are loaded by ReportsToEmployeeId.
        // Note: no Include(e => e.JobTitle) — load job titles separately for InMemory compatibility.

        var empQuery = _dbContext.Employees
            .AsNoTracking();

        if (!includeInactive)
            empQuery = empQuery.Where(e => e.IsActive);

        var allEmployees = await empQuery
            .OrderBy(e => e.FirstName)
            .ThenBy(e => e.LastName)
            .ToListAsync(cancellationToken);

        // Load job title names in a batch for display
        var jtIds = allEmployees.Select(e => e.JobTitleId).Distinct().ToList();
        var jobTitleNames = jtIds.Count > 0
            ? await _dbContext.JobTitles.AsNoTracking()
                .Where(j => jtIds.Contains(j.Id))
                .ToDictionaryAsync(j => j.Id, j => j.TitleName, cancellationToken)
            : new Dictionary<Guid, string>();

        // Build lookup by manager ID for tree construction.
        var childrenLookup = allEmployees.ToLookup(e => e.ReportsToEmployeeId);

        if (parentId.HasValue)
        {
            // Lazy load: return direct reports of the specified manager.
            var parentExists = allEmployees.Any(e => e.Id == parentId.Value);
            if (!parentExists)
                return Result<OrgTreeResult>.Success(new OrgTreeResult
                {
                    Nodes = [],
                    View = "reporting",
                    ReportingViewAvailable = true,
                });

            var directReports = childrenLookup[parentId.Value]
                .Select(e => BuildReportingNode(e, childrenLookup, jobTitleNames, depth, 1))
                .ToList();

            return Result<OrgTreeResult>.Success(new OrgTreeResult
            {
                Nodes = directReports,
                View = "reporting",
                ReportingViewAvailable = true,
            });
        }

        // Full tree: root nodes are employees with no manager.
        var rootNodes = childrenLookup[null]
            .Select(e => BuildReportingNode(e, childrenLookup, jobTitleNames, depth, 1))
            .ToList();

        return Result<OrgTreeResult>.Success(new OrgTreeResult
        {
            Nodes = rootNodes,
            View = "reporting",
            ReportingViewAvailable = true,
        });
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Recursively builds a department node with children limited by depth.
    /// </summary>
    private static OrgTreeNodeDto BuildDepartmentNode(
        Department dept,
        ILookup<Guid?, Department> childrenLookup,
        IReadOnlyDictionary<Guid, int> employeeCounts,
        int maxDepth,
        int currentDepth)
    {
        var childDepartments = childrenLookup[dept.Id].ToList();
        employeeCounts.TryGetValue(dept.Id, out var empCount);

        var children = currentDepth < maxDepth
            ? childDepartments
                .Select(child => BuildDepartmentNode(child, childrenLookup, employeeCounts, maxDepth, currentDepth + 1))
                .ToList()
            : [];

        return new OrgTreeNodeDto
        {
            NodeId = dept.Id,
            NodeType = "department",
            Name = dept.Name,
            Title = dept.Manager is not null ? $"{dept.Manager.FirstName} {dept.Manager.LastName}" : null,
            AvatarUrl = dept.Manager?.ProfilePhotoUrl,
            EmployeeCount = empCount,
            ChildrenCount = childDepartments.Count,
            ParentId = dept.ParentDepartmentId,
            IsActive = dept.IsActive,
            Children = children,
        };
    }

    /// <summary>
    /// Recursively builds a reporting-structure node from Employee.ReportsToEmployeeId (US-CHR-011).
    /// </summary>
    private static OrgTreeNodeDto BuildReportingNode(
        Employee emp,
        ILookup<Guid?, Employee> childrenLookup,
        Dictionary<Guid, string> jobTitleNames,
        int maxDepth,
        int currentDepth)
    {
        var directReports = childrenLookup[emp.Id].ToList();

        var children = currentDepth < maxDepth
            ? directReports
                .Select(child => BuildReportingNode(child, childrenLookup, jobTitleNames, maxDepth, currentDepth + 1))
                .ToList()
            : [];

        return new OrgTreeNodeDto
        {
            NodeId = emp.Id,
            NodeType = "employee",
            Name = $"{emp.FirstName} {emp.LastName}",
            Title = jobTitleNames.TryGetValue(emp.JobTitleId, out var jt) ? jt : null,
            AvatarUrl = emp.ProfilePhotoUrl,
            EmployeeCount = null,
            ChildrenCount = directReports.Count,
            ParentId = emp.ReportsToEmployeeId,
            IsActive = emp.IsActive,
            Children = children,
        };
    }
}
