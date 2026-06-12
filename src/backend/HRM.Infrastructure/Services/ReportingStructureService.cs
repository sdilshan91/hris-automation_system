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
/// Employee reporting structure service (US-CHR-011).
/// Manages manager assignment with cycle detection, audit trail, and bulk operations.
/// Cycle detection mirrors the Department ancestor-walk pattern from DepartmentService.
/// </summary>
public sealed class ReportingStructureService : IReportingStructureService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ReportingStructureService> _logger;

    /// <summary>Safety limit for cycle detection walk depth (NFR-2).</summary>
    private const int MaxChainDepth = 500;

    public ReportingStructureService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<ReportingStructureService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<AssignManagerResult>> AssignManagerAsync(
        Guid employeeId,
        AssignManagerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AssignManagerResult>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .Include(e => e.Manager)
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);

        if (employee is null)
            return Result<AssignManagerResult>.Failure("Employee not found.", 404);

        var previousManagerId = employee.ReportsToEmployeeId;
        var previousManagerName = employee.Manager is not null
            ? $"{employee.Manager.FirstName} {employee.Manager.LastName}"
            : null;

        // FR-8: Unassign manager (set to null)
        if (request.ManagerEmployeeId is null)
        {
            employee.ReportsToEmployeeId = null;

            RecordManagerChangeHistory(employee, previousManagerId, previousManagerName,
                null, "Not Assigned", request.Reason);

            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Manager unassigned. EmployeeId={EmployeeId}, PreviousManager={PreviousManagerId}, TenantId={TenantId}, By={User}",
                employeeId, previousManagerId, _tenantContext.TenantId, _currentUser.Email);

            return Result<AssignManagerResult>.Success(new AssignManagerResult
            {
                EmployeeId = employeeId,
                PreviousManagerId = previousManagerId,
                PreviousManagerName = previousManagerName,
                NewManagerId = null,
                NewManagerName = null,
                Message = "Manager unassigned successfully.",
            });
        }

        // BR-7: Self-assignment rejected
        if (request.ManagerEmployeeId.Value == employeeId)
            return Result<AssignManagerResult>.Failure(
                "An employee cannot be assigned as their own manager.", 400);

        // Load the proposed manager
        var manager = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.ManagerEmployeeId.Value, cancellationToken);

        if (manager is null)
            return Result<AssignManagerResult>.Failure("Manager employee not found.", 404);

        // BR-3: Only active employees can be assigned as managers
        if (manager.Status is not (EmployeeStatus.Active or EmployeeStatus.Probation))
            return Result<AssignManagerResult>.Failure(
                "Only active employees can be assigned as managers.", 400);

        // FR-3 / AC-3: Cycle detection — walk the proposed manager's chain upward
        var cycleResult = await DetectReportingCycleAsync(
            employeeId, employee, request.ManagerEmployeeId.Value, manager, cancellationToken);
        if (cycleResult.IsFailure)
            return Result<AssignManagerResult>.Failure(cycleResult.Error!, 400);

        // No change needed
        if (employee.ReportsToEmployeeId == request.ManagerEmployeeId.Value)
            return Result<AssignManagerResult>.Success(new AssignManagerResult
            {
                EmployeeId = employeeId,
                PreviousManagerId = previousManagerId,
                PreviousManagerName = previousManagerName,
                NewManagerId = request.ManagerEmployeeId.Value,
                NewManagerName = $"{manager.FirstName} {manager.LastName}",
                Message = "No change. The employee already reports to this manager.",
            });

        // Apply the assignment
        var newManagerName = $"{manager.FirstName} {manager.LastName}";
        employee.ReportsToEmployeeId = request.ManagerEmployeeId.Value;

        RecordManagerChangeHistory(employee, previousManagerId, previousManagerName,
            request.ManagerEmployeeId.Value, newManagerName, request.Reason);

        // Audit log entry
        _dbContext.EmployeeFieldAuditLogs.Add(new EmployeeFieldAuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employeeId,
            Section = "ManagerAssignment",
            BeforeSnapshot = JsonSerializer.Serialize(new { ManagerId = previousManagerId, ManagerName = previousManagerName }),
            AfterSnapshot = JsonSerializer.Serialize(new { ManagerId = request.ManagerEmployeeId.Value, ManagerName = newManagerName }),
            ChangedByUserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            ChangedBy = _currentUser.IsAuthenticated ? _currentUser.Email : "system",
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Manager assigned. EmployeeId={EmployeeId}, NewManager={NewManagerId}, PreviousManager={PreviousManagerId}, TenantId={TenantId}, By={User}",
            employeeId, request.ManagerEmployeeId.Value, previousManagerId, _tenantContext.TenantId, _currentUser.Email);

        return Result<AssignManagerResult>.Success(new AssignManagerResult
        {
            EmployeeId = employeeId,
            PreviousManagerId = previousManagerId,
            PreviousManagerName = previousManagerName,
            NewManagerId = request.ManagerEmployeeId.Value,
            NewManagerName = newManagerName,
            Message = $"Manager assigned successfully. {employee.FirstName} {employee.LastName} now reports to {newManagerName}.",
        });
    }

    /// <inheritdoc />
    public async Task<Result<BulkAssignManagerResult>> BulkAssignManagerAsync(
        BulkAssignManagerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BulkAssignManagerResult>.Failure("Tenant context is not resolved.", 400);

        if (request.EmployeeIds.Count == 0)
            return Result<BulkAssignManagerResult>.Failure("No employee IDs provided.", 400);

        if (request.EmployeeIds.Count > 100)
            return Result<BulkAssignManagerResult>.Failure(
                "Bulk assignment is limited to 100 employees per request (NFR-6).", 400);

        var results = new List<BulkAssignManagerItemResult>();

        foreach (var employeeId in request.EmployeeIds)
        {
            var assignRequest = new AssignManagerRequest
            {
                ManagerEmployeeId = request.ManagerEmployeeId,
                Reason = request.Reason,
            };

            var result = await AssignManagerAsync(employeeId, assignRequest, cancellationToken);

            results.Add(new BulkAssignManagerItemResult
            {
                EmployeeId = employeeId,
                Success = result.IsSuccess,
                Error = result.Error,
                EmployeeName = result.Value?.Message,
            });
        }

        var successCount = results.Count(r => r.Success);
        var failureCount = results.Count(r => !r.Success);

        _logger.LogInformation(
            "Bulk manager assignment completed. Manager={ManagerId}, Total={Total}, Success={Success}, Failure={Failure}, TenantId={TenantId}",
            request.ManagerEmployeeId, request.EmployeeIds.Count, successCount, failureCount, _tenantContext.TenantId);

        return Result<BulkAssignManagerResult>.Success(new BulkAssignManagerResult
        {
            TotalRequested = request.EmployeeIds.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            Results = results,
        });
    }

    /// <inheritdoc />
    public async Task<Result<DirectReportsResult>> GetDirectReportsAsync(
        Guid managerId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<DirectReportsResult>.Failure("Tenant context is not resolved.", 400);

        var manager = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == managerId, cancellationToken);

        if (manager is null)
            return Result<DirectReportsResult>.Failure("Manager employee not found.", 404);

        // Load without Include to avoid InMemory FK resolution issues.
        // Navigation property access in the in-memory projection is safe;
        // on PostgreSQL the IQueryable .Select() would generate a JOIN.
        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.ReportsToEmployeeId == managerId)
            .OrderBy(e => e.FirstName)
            .ThenBy(e => e.LastName)
            .ToListAsync(cancellationToken);

        // Load department and job title names separately for non-null navigation props
        var deptIds = employees.Where(e => e.DepartmentId != Guid.Empty).Select(e => e.DepartmentId).Distinct().ToList();
        var jtIds = employees.Where(e => e.JobTitleId != Guid.Empty).Select(e => e.JobTitleId).Distinct().ToList();

        var deptNames = deptIds.Count > 0
            ? await _dbContext.Departments.AsNoTracking()
                .Where(d => deptIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken)
            : new Dictionary<Guid, string>();

        var jtNames = jtIds.Count > 0
            ? await _dbContext.JobTitles.AsNoTracking()
                .Where(j => jtIds.Contains(j.Id))
                .ToDictionaryAsync(j => j.Id, j => j.TitleName, cancellationToken)
            : new Dictionary<Guid, string>();

        var directReports = employees.Select(e => new DirectReportDto
        {
            Id = e.Id,
            EmployeeNo = e.EmployeeNo,
            FirstName = e.FirstName,
            LastName = e.LastName,
            JobTitle = jtNames.GetValueOrDefault(e.JobTitleId),
            DepartmentName = deptNames.GetValueOrDefault(e.DepartmentId),
            Status = e.Status.ToString(),
            AvatarUrl = e.ProfilePhotoUrl,
        }).ToList();

        return Result<DirectReportsResult>.Success(new DirectReportsResult
        {
            ManagerId = managerId,
            ManagerName = $"{manager.FirstName} {manager.LastName}",
            DirectReports = directReports,
        });
    }

    // ── Private helpers ──────────────────────────────────────────────

    /// <summary>
    /// Detects circular reporting chains by walking the proposed manager's
    /// reporting chain upward. If the employee being assigned appears in the
    /// chain, a cycle would be created (FR-3, AC-3).
    /// Mirrors the DepartmentService.DetectCycleAsync ancestor-walk pattern.
    /// </summary>
    private async Task<Result> DetectReportingCycleAsync(
        Guid employeeId,
        Employee employee,
        Guid proposedManagerId,
        Employee proposedManager,
        CancellationToken cancellationToken)
    {
        var currentId = proposedManagerId;
        var depth = 0;

        while (currentId != Guid.Empty && depth < MaxChainDepth)
        {
            if (currentId == employeeId)
            {
                // Build the rejection message per AC-3 format
                var employeeName = $"{employee.FirstName} {employee.LastName}";
                var managerName = $"{proposedManager.FirstName} {proposedManager.LastName}";
                return Result.Failure(
                    $"Circular reporting chain detected. {employeeName} cannot report to {managerName} " +
                    $"because {managerName} already reports to {employeeName} (directly or indirectly).");
            }

            var parent = await _dbContext.Employees
                .AsNoTracking()
                .Where(e => e.Id == currentId)
                .Select(e => new { e.ReportsToEmployeeId })
                .FirstOrDefaultAsync(cancellationToken);

            if (parent?.ReportsToEmployeeId is null)
                break;

            currentId = parent.ReportsToEmployeeId.Value;
            depth++;
        }

        return Result.Success();
    }

    /// <summary>
    /// Records a manager change in the employment history timeline (FR-6, AC-2, NFR-5).
    /// </summary>
    private void RecordManagerChangeHistory(
        Employee employee,
        Guid? previousManagerId,
        string? previousManagerName,
        Guid? newManagerId,
        string newManagerName,
        string? reason)
    {
        _dbContext.EmploymentHistories.Add(new EmploymentHistory
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employee.Id,
            ChangeType = "manager_change",
            PreviousValue = previousManagerName ?? "Not Assigned",
            NewValue = newManagerName,
            PreviousReferenceId = previousManagerId,
            NewReferenceId = newManagerId,
            EffectiveDate = DateTime.UtcNow.Date,
            Reason = reason,
            ChangedBy = _currentUser.IsAuthenticated ? _currentUser.Email : "system",
        });
    }
}
