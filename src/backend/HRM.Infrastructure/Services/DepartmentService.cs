using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Department CRUD service (US-CHR-004).
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// </summary>
public sealed class DepartmentService : IDepartmentService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<DepartmentService> _logger;

    public DepartmentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<DepartmentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new department (AC-2). Validates name/code uniqueness (AC-3, FR-2, BR-1)
    /// and parent belongs to same tenant (BR-3).
    /// </summary>
    public async Task<Result<DepartmentDto>> CreateAsync(
        string name, string code, string? description,
        Guid? parentDepartmentId, Guid? managerId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<DepartmentDto>.Failure("Tenant context is not resolved.", 400);

        // FR-2 / BR-1: name uniqueness within tenant
        var nameExists = await _dbContext.Departments
            .AnyAsync(d => d.Name == name, cancellationToken);

        if (nameExists)
            return Result<DepartmentDto>.Failure("A department with this name already exists.", 400);

        // Code uniqueness within tenant
        var codeExists = await _dbContext.Departments
            .AnyAsync(d => d.Code == code, cancellationToken);

        if (codeExists)
            return Result<DepartmentDto>.Failure($"A department with code '{code}' already exists in this tenant.", 400);

        // BR-3: parent must belong to the same tenant (the global filter ensures this)
        if (parentDepartmentId.HasValue)
        {
            var parentExists = await _dbContext.Departments
                .AnyAsync(d => d.Id == parentDepartmentId.Value && d.IsActive, cancellationToken);

            if (!parentExists)
                return Result<DepartmentDto>.Failure("Parent department not found or is not active.", 400);
        }

        var department = new Department
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Name = name,
            Code = code,
            Description = description,
            ParentDepartmentId = parentDepartmentId,
            ManagerId = managerId,
            IsActive = true,
            IsDeleted = false,
        };

        _dbContext.Departments.Add(department);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Department created. Id={DepartmentId}, Name={DepartmentName}, Code={Code}, TenantId={TenantId}, By={User}",
            department.Id, department.Name, department.Code, _tenantContext.TenantId, _currentUser.Email);

        // Reload with parent to populate ParentDepartmentName
        return Result<DepartmentDto>.Success(await LoadDtoAsync(department.Id, cancellationToken));
    }

    /// <summary>
    /// Updates a department (AC-4). Validates hierarchy changes for circular refs (FR-5)
    /// and name/code uniqueness excluding self.
    /// </summary>
    public async Task<Result<DepartmentDto>> UpdateAsync(
        Guid departmentId, string name, string code, string? description,
        Guid? parentDepartmentId, Guid? managerId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<DepartmentDto>.Failure("Tenant context is not resolved.", 400);

        var department = await _dbContext.Departments
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken);

        if (department is null)
            return Result<DepartmentDto>.Failure("Department not found.", 404);

        // Name uniqueness (excluding self)
        var nameExists = await _dbContext.Departments
            .AnyAsync(d => d.Name == name && d.Id != departmentId, cancellationToken);

        if (nameExists)
            return Result<DepartmentDto>.Failure("A department with this name already exists.", 400);

        // Code uniqueness (excluding self)
        var codeExists = await _dbContext.Departments
            .AnyAsync(d => d.Code == code && d.Id != departmentId, cancellationToken);

        if (codeExists)
            return Result<DepartmentDto>.Failure($"A department with code '{code}' already exists in this tenant.", 400);

        // Self-referencing check
        if (parentDepartmentId.HasValue && parentDepartmentId.Value == departmentId)
            return Result<DepartmentDto>.Failure("A department cannot be its own parent.", 400);

        // Validate parent exists and is active
        if (parentDepartmentId.HasValue)
        {
            var parentExists = await _dbContext.Departments
                .AnyAsync(d => d.Id == parentDepartmentId.Value && d.IsActive, cancellationToken);

            if (!parentExists)
                return Result<DepartmentDto>.Failure("Parent department not found or is not active.", 400);

            // FR-5: Circular reference detection - walk up the ancestor chain
            var circularCheckResult = await DetectCycleAsync(departmentId, parentDepartmentId.Value, cancellationToken);
            if (circularCheckResult.IsFailure)
                return Result<DepartmentDto>.Failure(circularCheckResult.Error!, 400);
        }

        department.Name = name;
        department.Code = code;
        department.Description = description;
        department.ParentDepartmentId = parentDepartmentId;
        department.ManagerId = managerId;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Department updated. Id={DepartmentId}, Name={DepartmentName}, TenantId={TenantId}, By={User}",
            department.Id, department.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result<DepartmentDto>.Success(await LoadDtoAsync(department.Id, cancellationToken));
    }

    /// <summary>
    /// Deactivates (soft-deletes) a department (AC-5, FR-6, FR-7, BR-6).
    /// Blocks if the department has active children.
    /// NOTE: Employee assignment check is deferred to US-CHR-001.
    /// </summary>
    public async Task<Result> DeactivateAsync(
        Guid departmentId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var department = await _dbContext.Departments
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken);

        if (department is null)
            return Result.Failure("Department not found.", 404);

        if (!department.IsActive)
            return Result.Failure("Department is already deactivated.", 400);

        // BR-6: Cannot deactivate a department that has active children
        var activeChildCount = await _dbContext.Departments
            .CountAsync(d => d.ParentDepartmentId == departmentId && d.IsActive, cancellationToken);

        if (activeChildCount > 0)
            return Result.Failure(
                $"Cannot deactivate this department. It has {activeChildCount} active child department(s). Please reassign or deactivate them first.",
                400);

        // AC-5 / FR-6: Cannot deactivate if there are active employees assigned.
        // TODO(US-CHR-001): When Employee entity exists, add:
        //   var activeEmployeeCount = await _dbContext.Employees
        //       .CountAsync(e => e.DepartmentId == departmentId && e.IsActive, ct);
        //   if (activeEmployeeCount > 0)
        //       return Result.Failure($"This department has {activeEmployeeCount} active employees...");

        department.IsActive = false;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Department deactivated. Id={DepartmentId}, Name={DepartmentName}, TenantId={TenantId}, By={User}",
            department.Id, department.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    public async Task<Result<DepartmentDto>> GetByIdAsync(
        Guid departmentId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<DepartmentDto>.Failure("Tenant context is not resolved.", 400);

        var department = await _dbContext.Departments
            .Include(d => d.ParentDepartment)
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == departmentId, cancellationToken);

        if (department is null)
            return Result<DepartmentDto>.Failure("Department not found.", 404);

        return Result<DepartmentDto>.Success(ToDto(department));
    }

    public async Task<Result<IReadOnlyList<DepartmentDto>>> GetAllAsync(
        bool? activeOnly = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<DepartmentDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.Departments
            .Include(d => d.ParentDepartment)
            .AsNoTracking();

        if (activeOnly == true)
            query = query.Where(d => d.IsActive);

        var departments = await query
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        var dtos = departments.Select(ToDto).ToList();
        return Result<IReadOnlyList<DepartmentDto>>.Success(dtos);
    }

    /// <summary>
    /// Returns the full department hierarchy as a tree (FR-8).
    /// Loads all active departments in one query and builds the tree in-memory.
    /// </summary>
    public async Task<Result<IReadOnlyList<DepartmentTreeNodeDto>>> GetTreeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<DepartmentTreeNodeDto>>.Failure("Tenant context is not resolved.", 400);

        var allDepartments = await _dbContext.Departments
            .Where(d => d.IsActive)
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

        var tree = BuildTree(allDepartments);
        return Result<IReadOnlyList<DepartmentTreeNodeDto>>.Success(tree);
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Detects circular references by walking up the ancestor chain from the
    /// proposed parent. If we encounter the department being moved, it's a cycle (FR-5).
    /// </summary>
    private async Task<Result> DetectCycleAsync(
        Guid departmentId, Guid proposedParentId, CancellationToken cancellationToken)
    {
        var currentId = proposedParentId;
        var visited = new HashSet<Guid> { departmentId };
        const int maxDepth = 50; // safety limit
        var depth = 0;

        while (currentId != Guid.Empty && depth < maxDepth)
        {
            if (visited.Contains(currentId))
                return Result.Failure("Circular reference detected. A department cannot be its own ancestor.");

            visited.Add(currentId);

            var parent = await _dbContext.Departments
                .AsNoTracking()
                .Where(d => d.Id == currentId)
                .Select(d => new { d.ParentDepartmentId })
                .FirstOrDefaultAsync(cancellationToken);

            if (parent?.ParentDepartmentId is null)
                break;

            currentId = parent.ParentDepartmentId.Value;
            depth++;
        }

        return Result.Success();
    }

    /// <summary>
    /// Builds a tree from a flat list of departments.
    /// Root departments (ParentDepartmentId == null) are top-level nodes.
    /// </summary>
    private static IReadOnlyList<DepartmentTreeNodeDto> BuildTree(IReadOnlyList<Department> departments)
    {
        var lookup = departments.ToLookup(d => d.ParentDepartmentId);

        DepartmentTreeNodeDto ToNode(Department d) => new()
        {
            Id = d.Id,
            Name = d.Name,
            Code = d.Code,
            Description = d.Description,
            ManagerId = d.ManagerId,
            IsActive = d.IsActive,
            Children = lookup[d.Id].Select(ToNode).ToList(),
        };

        // Root departments: those with no parent
        return lookup[null].Select(ToNode).ToList();
    }

    private async Task<DepartmentDto> LoadDtoAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        var department = await _dbContext.Departments
            .Include(d => d.ParentDepartment)
            .AsNoTracking()
            .FirstAsync(d => d.Id == departmentId, cancellationToken);

        return ToDto(department);
    }

    private static DepartmentDto ToDto(Department d) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Code = d.Code,
        Description = d.Description,
        ParentDepartmentId = d.ParentDepartmentId,
        ParentDepartmentName = d.ParentDepartment?.Name,
        ManagerId = d.ManagerId,
        IsActive = d.IsActive,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
    };
}
