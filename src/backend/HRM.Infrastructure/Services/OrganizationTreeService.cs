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

        // NOTE: Employee.ReportsTo FK does not exist yet (deferred to US-CHR-011).
        // Best-effort implementation: show department managers as root-level "manager" nodes,
        // with their department's employees as children.
        // This gives the client a meaningful reporting view using available data.

        var deptQuery = _dbContext.Departments
            .Include(d => d.Manager)
                .ThenInclude(m => m!.JobTitle)
            .AsNoTracking();

        if (!includeInactive)
            deptQuery = deptQuery.Where(d => d.IsActive);

        var departments = await deptQuery
            .Where(d => d.ManagerId != null)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        // For each department with a manager, load that department's employees.
        var deptIds = departments.Select(d => d.Id).ToHashSet();

        var empQuery = _dbContext.Employees
            .Include(e => e.JobTitle)
            .AsNoTracking();

        if (!includeInactive)
            empQuery = empQuery.Where(e => e.IsActive);

        var employees = await empQuery
            .Where(e => deptIds.Contains(e.DepartmentId))
            .OrderBy(e => e.FirstName)
            .ThenBy(e => e.LastName)
            .ToListAsync(cancellationToken);

        var employeesByDept = employees.ToLookup(e => e.DepartmentId);

        if (parentId.HasValue)
        {
            // Lazy load: parentId should be an employee (manager) ID.
            // Find the department managed by this employee and return their reports.
            var managedDept = departments.FirstOrDefault(d => d.ManagerId == parentId.Value);
            if (managedDept is null)
                return Result<OrgTreeResult>.Success(new OrgTreeResult
                {
                    Nodes = [],
                    View = "reporting",
                    ReportingViewAvailable = false,
                });

            var directReports = employeesByDept[managedDept.Id]
                .Where(e => e.Id != parentId.Value) // exclude the manager themselves
                .Select(e => ToEmployeeNode(e, parentId.Value))
                .ToList();

            return Result<OrgTreeResult>.Success(new OrgTreeResult
            {
                Nodes = directReports,
                View = "reporting",
                ReportingViewAvailable = false,
            });
        }

        // Build manager nodes with their direct reports (depth-limited).
        var managerNodes = new List<OrgTreeNodeDto>();

        foreach (var dept in departments)
        {
            var manager = dept.Manager!;
            var reports = employeesByDept[dept.Id]
                .Where(e => e.Id != manager.Id) // exclude the manager from their own reports
                .ToList();

            var children = depth > 1
                ? reports.Select(e => ToEmployeeNode(e, manager.Id)).ToList()
                : [];

            managerNodes.Add(new OrgTreeNodeDto
            {
                NodeId = manager.Id,
                NodeType = "employee",
                Name = $"{manager.FirstName} {manager.LastName}",
                Title = manager.JobTitle?.TitleName,
                AvatarUrl = manager.ProfilePhotoUrl,
                EmployeeCount = null,
                ChildrenCount = reports.Count,
                ParentId = null,
                IsActive = manager.IsActive,
                Children = children,
            });
        }

        return Result<OrgTreeResult>.Success(new OrgTreeResult
        {
            Nodes = managerNodes,
            View = "reporting",
            ReportingViewAvailable = false, // Deferred: true reporting chain requires Employee.ReportsTo (US-CHR-011)
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

    private static OrgTreeNodeDto ToEmployeeNode(Employee emp, Guid parentManagerId)
    {
        return new OrgTreeNodeDto
        {
            NodeId = emp.Id,
            NodeType = "employee",
            Name = $"{emp.FirstName} {emp.LastName}",
            Title = emp.JobTitle?.TitleName,
            AvatarUrl = emp.ProfilePhotoUrl,
            EmployeeCount = null,
            ChildrenCount = 0, // Leaf node; true reporting chain deferred to US-CHR-011
            ParentId = parentManagerId,
            IsActive = emp.IsActive,
            Children = [],
        };
    }
}
