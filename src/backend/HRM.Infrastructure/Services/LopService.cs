using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Compulsory-leave / Loss-of-Pay (LOP) handling (US-LV-011). All queries are tenant-scoped via
/// ITenantContext and EF global query filters. LOP entries are leave_request rows with is_lop = true
/// against the system LOP leave type and carry NO balance impact (BR-1).
/// </summary>
public sealed class LopService : ILopService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILeaveTypeService _leaveTypeService;
    private readonly IAttendanceProvider _attendanceProvider;
    private readonly ILeaveNotificationService _notificationService;
    private readonly ILogger<LopService> _logger;

    // FR-6: page size for the compulsory-leave employee scan (mirrors LeaveAccrualJob's batching).
    private const int EmployeePageSize = 500;

    public LopService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILeaveTypeService leaveTypeService,
        IAttendanceProvider attendanceProvider,
        ILeaveNotificationService notificationService,
        ILogger<LopService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _leaveTypeService = leaveTypeService;
        _attendanceProvider = attendanceProvider;
        _notificationService = notificationService;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  AC-3 / FR-3: manual HR LOP assignment
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<AssignLopResultDto>> AssignLopAsync(
        AssignLopRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AssignLopResultDto>.Failure("Tenant context is not resolved.", 400);

        if (request.Dates is null || request.Dates.Count == 0)
            return Result<AssignLopResultDto>.Failure("At least one date is required.", 400);

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<AssignLopResultDto>.Failure("Employee not found.", 404);

        var lopTypeResult = await _leaveTypeService.EnsureLopTypeForTenantAsync(
            _tenantContext.TenantId, cancellationToken);
        if (lopTypeResult.IsFailure)
            return Result<AssignLopResultDto>.Failure(lopTypeResult.Error!, lopTypeResult.StatusCode ?? 400);
        var lopTypeId = lopTypeResult.Value;

        var distinctDates = request.Dates.Distinct().OrderBy(d => d).ToList();
        var existingLopDates = await ExistingLopDatesAsync(request.EmployeeId, distinctDates, cancellationToken);

        var created = new List<Guid>();
        var skipped = new List<DateOnly>();

        foreach (var date in distinctDates)
        {
            if (existingLopDates.Contains(date))
            {
                skipped.Add(date);
                continue;
            }

            var lop = NewLopRequest(
                request.EmployeeId, lopTypeId, date, LeaveRequestStatus.HrAssigned,
                Domain.Enums.LopSource.HrAssigned, request.Reason);
            _dbContext.LeaveRequests.Add(lop);

            // FR-6 / NFR-4: approval-history row records the HR action (actor = the assigned employee's
            // record is not the actor; we log the assignment as a history row keyed by the request).
            _dbContext.LeaveApprovalHistories.Add(new LeaveApprovalHistory
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                LeaveRequestId = lop.Id,
                ApproverEmployeeId = request.EmployeeId,
                ApprovalLevel = 1,
                Action = LeaveApprovalAction.Approved,
                Comment = request.Reason is null ? "LOP assigned by HR" : $"LOP assigned by HR: {request.Reason}",
                ActionedAt = DateTime.UtcNow,
            });

            created.Add(lop.Id);
        }

        if (created.Count > 0)
            await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "HR assigned {Count} LOP day(s) to employee {EmployeeId} ({Skipped} skipped) in tenant {TenantId}. Action {AuditAction}.",
            created.Count, request.EmployeeId, skipped.Count, _tenantContext.TenantId, "Leave.LopAssigned");

        // BR-6: notify the employee (log-only seam).
        if (created.Count > 0)
            await _notificationService.NotifyLopAssignedAsync(
                request.EmployeeId, Domain.Enums.LopSource.HrAssigned.ToString(),
                created.Count, request.Reason, cancellationToken);

        return Result<AssignLopResultDto>.Success(new AssignLopResultDto
        {
            EmployeeId = request.EmployeeId,
            LeaveTypeId = lopTypeId,
            CreatedCount = created.Count,
            SkippedDates = skipped,
            RequestIds = created,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-5: payroll LOP summary (read-only)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<LopSummaryDto>> GetLopSummaryAsync(
        Guid employeeId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LopSummaryDto>.Failure("Tenant context is not resolved.", 400);
        if (to < from)
            return Result<LopSummaryDto>.Failure("'to' must be on or after 'from'.", 400);

        // LOP entries overlapping the period. Cancelled/Rejected are excluded (no longer effective).
        var lopRequests = await _dbContext.LeaveRequests
            .AsNoTracking()
            .Where(lr => lr.IsLop
                         && lr.EmployeeId == employeeId
                         && lr.Status != LeaveRequestStatus.Cancelled
                         && lr.Status != LeaveRequestStatus.Rejected
                         && lr.StartDate <= to && lr.EndDate >= from)
            .OrderBy(lr => lr.StartDate)
            .ToListAsync(cancellationToken);

        var entries = lopRequests.Select(lr => new LopEntryDto
        {
            RequestId = lr.Id,
            StartDate = lr.StartDate,
            EndDate = lr.EndDate,
            Days = lr.TotalDays,
            Source = lr.LopSource?.ToString() ?? string.Empty,
            Status = lr.Status.ToString(),
            Reason = lr.Reason,
        }).ToList();

        return Result<LopSummaryDto>.Success(new LopSummaryDto
        {
            EmployeeId = employeeId,
            From = from,
            To = to,
            TotalLopDays = entries.Sum(e => e.Days),
            Entries = entries,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-6 / BR-4: compulsory leave (company shutdown) bulk assign
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<CompulsoryLeaveResultDto>> AssignCompulsoryLeaveAsync(
        CompulsoryLeaveRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CompulsoryLeaveResultDto>.Failure("Tenant context is not resolved.", 400);
        if (request.Dates is null || request.Dates.Count == 0)
            return Result<CompulsoryLeaveResultDto>.Failure("At least one date is required.", 400);

        var leaveType = await _dbContext.LeaveTypes
            .FirstOrDefaultAsync(lt => lt.Id == request.LeaveTypeId, cancellationToken);
        if (leaveType is null)
            return Result<CompulsoryLeaveResultDto>.Failure("Leave type not found.", 404);

        var lopTypeResult = await _leaveTypeService.EnsureLopTypeForTenantAsync(
            _tenantContext.TenantId, cancellationToken);
        if (lopTypeResult.IsFailure)
            return Result<CompulsoryLeaveResultDto>.Failure(lopTypeResult.Error!, lopTypeResult.StatusCode ?? 400);
        var lopTypeId = lopTypeResult.Value;

        var distinctDates = request.Dates.Distinct().OrderBy(d => d).ToList();

        // Persist one CompulsoryLeave anchor row per date (FR-6 §7).
        var anchors = new Dictionary<DateOnly, CompulsoryLeave>();
        foreach (var date in distinctDates)
        {
            var anchor = new CompulsoryLeave
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                Date = date,
                LeaveTypeId = request.LeaveTypeId,
                Reason = request.Reason,
            };
            anchors[date] = anchor;
            _dbContext.CompulsoryLeaves.Add(anchor);
        }

        int employeesProcessed = 0;
        int assignedCount = 0;
        int lopCount = 0;
        var notifyLopEmployees = new HashSet<Guid>();

        // Build the employee query (all active, or the selected subset). Page to stay reasonable.
        var baseQuery = _dbContext.Employees
            .Where(e => e.Status != EmployeeStatus.Terminated);
        if (request.EmployeeIds is { Count: > 0 })
        {
            var idSet = request.EmployeeIds.ToHashSet();
            baseQuery = baseQuery.Where(e => idSet.Contains(e.Id));
        }
        baseQuery = baseQuery.OrderBy(e => e.Id);

        int page = 0;
        while (true)
        {
            var employees = await baseQuery
                .Skip(page * EmployeePageSize)
                .Take(EmployeePageSize)
                .ToListAsync(cancellationToken);
            if (employees.Count == 0)
                break;

            foreach (var employee in employees)
            {
                employeesProcessed++;
                foreach (var date in distinctDates)
                {
                    // Skip dates already covered by an existing (non-cancelled) request for this employee.
                    bool alreadyCovered = await _dbContext.LeaveRequests.AnyAsync(lr =>
                        lr.EmployeeId == employee.Id
                        && lr.Status != LeaveRequestStatus.Cancelled
                        && lr.Status != LeaveRequestStatus.Rejected
                        && lr.StartDate <= date && lr.EndDate >= date,
                        cancellationToken);
                    if (alreadyCovered)
                        continue;

                    // BR-4: deduct from balance first; if insufficient, fall back to LOP.
                    int leaveYear = date.Year;
                    decimal balance = await GetLedgerBalanceAsync(
                        employee.Id, request.LeaveTypeId, leaveYear, cancellationToken);

                    if (balance >= 1m)
                    {
                        // Deduct one day from the chosen leave type: create an Approved request + Used ledger.
                        var leaveReq = new LeaveRequest
                        {
                            Id = BaseEntity.NewUuidV7(),
                            TenantId = _tenantContext.TenantId,
                            EmployeeId = employee.Id,
                            LeaveTypeId = request.LeaveTypeId,
                            StartDate = date,
                            EndDate = date,
                            TotalDays = 1m,
                            Reason = request.Reason ?? "Compulsory leave (company shutdown)",
                            Status = LeaveRequestStatus.Approved,
                            RequestedAt = DateTime.UtcNow,
                        };
                        _dbContext.LeaveRequests.Add(leaveReq);

                        _dbContext.LeaveLedgerEntries.Add(new LeaveLedger
                        {
                            Id = BaseEntity.NewUuidV7(),
                            TenantId = _tenantContext.TenantId,
                            EntryType = LedgerEntryType.Used,
                            EmployeeId = employee.Id,
                            LeaveTypeId = request.LeaveTypeId,
                            LeaveYear = leaveYear,
                            LeaveRequestId = leaveReq.Id,
                            Amount = -1m,
                            BalanceAfter = balance - 1m,
                            Description = $"Compulsory leave ({leaveType.Name}) on {date:yyyy-MM-dd}",
                            OccurredAt = DateTime.UtcNow,
                        });
                    }
                    else
                    {
                        // Insufficient balance -> LOP (lop_source = Compulsory).
                        var lop = NewLopRequest(
                            employee.Id, lopTypeId, date, LeaveRequestStatus.HrAssigned,
                            Domain.Enums.LopSource.Compulsory, request.Reason ?? "Compulsory leave (insufficient balance)");
                        _dbContext.LeaveRequests.Add(lop);
                        lopCount++;
                        notifyLopEmployees.Add(employee.Id);
                        anchors[date].LopCount++;
                    }

                    assignedCount++;
                    anchors[date].AssignedCount++;
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (employees.Count < EmployeePageSize)
                break;
            page++;
        }

        _logger.LogInformation(
            "Compulsory leave assigned: type {LeaveTypeId}, {Dates} date(s), {Employees} employee(s), " +
            "{Assigned} row(s) ({Lop} LOP) in tenant {TenantId}. Action {AuditAction}.",
            request.LeaveTypeId, distinctDates.Count, employeesProcessed, assignedCount, lopCount,
            _tenantContext.TenantId, "Leave.CompulsoryAssigned");

        // BR-6: notify employees who fell back to LOP.
        foreach (var empId in notifyLopEmployees)
            await _notificationService.NotifyLopAssignedAsync(
                empId, Domain.Enums.LopSource.Compulsory.ToString(),
                distinctDates.Count, request.Reason, cancellationToken);

        return Result<CompulsoryLeaveResultDto>.Success(new CompulsoryLeaveResultDto
        {
            LeaveTypeId = request.LeaveTypeId,
            Dates = distinctDates,
            EmployeesProcessed = employeesProcessed,
            AssignedCount = assignedCount,
            LopCount = lopCount,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-2 / AC-2: auto-LOP from absenteeism (seam-driven)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<int>> GenerateAbsenteeismLopAsync(
        Guid employeeId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<int>.Failure("Tenant context is not resolved.", 400);
        if (to < from)
            return Result<int>.Failure("'to' must be on or after 'from'.", 400);

        // SEAM: ask the attendance provider for absent working days. With NoOpAttendanceProvider
        // (no attendance module yet) this returns empty, so nothing is generated. The job is still
        // wired, idempotent, and tenant-safe (US-LV-011 FR-2 deferral).
        var absentDays = await _attendanceProvider.GetAbsentWorkingDaysAsync(
            employeeId, from, to, cancellationToken);
        if (absentDays.Count == 0)
            return Result<int>.Success(0);

        var lopTypeResult = await _leaveTypeService.EnsureLopTypeForTenantAsync(
            _tenantContext.TenantId, cancellationToken);
        if (lopTypeResult.IsFailure)
            return Result<int>.Failure(lopTypeResult.Error!, lopTypeResult.StatusCode ?? 400);
        var lopTypeId = lopTypeResult.Value;

        var existingLopDates = await ExistingLopDatesAsync(
            employeeId, absentDays.OrderBy(d => d).ToList(), cancellationToken);

        int created = 0;
        foreach (var date in absentDays.OrderBy(d => d))
        {
            if (existingLopDates.Contains(date))
                continue; // idempotent

            var lop = NewLopRequest(
                employeeId, lopTypeId, date, LeaveRequestStatus.SystemGenerated,
                Domain.Enums.LopSource.SystemGenerated, "Auto-generated LOP for unaccounted absence");
            _dbContext.LeaveRequests.Add(lop);
            created++;
        }

        if (created > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _notificationService.NotifyLopAssignedAsync(
                employeeId, Domain.Enums.LopSource.SystemGenerated.ToString(),
                created, "Unaccounted absence", cancellationToken);
        }

        return Result<int>.Success(created);
    }

    // ══════════════════════════════════════════════════════════════
    //  BR-3: HR overrides a system-generated LOP entry
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<OverrideLopResultDto>> OverrideLopAsync(
        Guid leaveRequestId,
        OverrideLopRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<OverrideLopResultDto>.Failure("Tenant context is not resolved.", 400);

        var lopRequest = await _dbContext.LeaveRequests
            .FirstOrDefaultAsync(lr => lr.Id == leaveRequestId, cancellationToken);
        if (lopRequest is null)
            return Result<OverrideLopResultDto>.Failure("Leave request not found.", 404);
        if (!lopRequest.IsLop)
            return Result<OverrideLopResultDto>.Failure("Only LOP entries can be overridden.", 400);
        if (lopRequest.Status == LeaveRequestStatus.Cancelled)
            return Result<OverrideLopResultDto>.Failure("This LOP entry has already been removed.", 409);

        Guid? ledgerEntryId = null;

        if (request.TargetLeaveTypeId is null)
        {
            // Remove the LOP entry (soft-cancel). No balance was deducted (BR-1), so no ledger change.
            lopRequest.Status = LeaveRequestStatus.Cancelled;
            lopRequest.CancelledAt = DateTime.UtcNow;
            lopRequest.CancellationReason = request.Reason ?? "LOP removed by HR";
        }
        else
        {
            var targetType = await _dbContext.LeaveTypes
                .FirstOrDefaultAsync(lt => lt.Id == request.TargetLeaveTypeId.Value, cancellationToken);
            if (targetType is null)
                return Result<OverrideLopResultDto>.Failure("Target leave type not found.", 404);
            if (targetType.SystemCategory == LeaveTypeSystemCategory.LossOfPay)
                return Result<OverrideLopResultDto>.Failure("Cannot convert an LOP entry to the LOP type.", 400);

            // Convert to a balance-backed type: flip type, clear LOP, set Approved, and apply a Used
            // deduction so the converted leave consumes balance (BR-3 — "convert to a different type").
            int leaveYear = lopRequest.StartDate.Year;
            decimal balance = await GetLedgerBalanceAsync(
                lopRequest.EmployeeId, targetType.Id, leaveYear, cancellationToken);
            decimal projected = balance - lopRequest.TotalDays;

            if (projected < 0m && !targetType.NegativeBalanceAllowed)
                return Result<OverrideLopResultDto>.Failure(
                    $"Insufficient {targetType.Name} balance to convert this LOP entry. " +
                    $"Available: {balance} day(s), required: {lopRequest.TotalDays} day(s).", 400);

            lopRequest.LeaveTypeId = targetType.Id;
            lopRequest.IsLop = false;
            lopRequest.LopSource = null;
            lopRequest.Status = LeaveRequestStatus.Approved;
            lopRequest.Reason = request.Reason ?? lopRequest.Reason;

            var ledger = new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                EntryType = LedgerEntryType.Used,
                EmployeeId = lopRequest.EmployeeId,
                LeaveTypeId = targetType.Id,
                LeaveYear = leaveYear,
                LeaveRequestId = lopRequest.Id,
                Amount = -lopRequest.TotalDays,
                BalanceAfter = projected,
                Description = $"LOP converted to {targetType.Name}: {lopRequest.TotalDays} day(s)",
                OccurredAt = DateTime.UtcNow,
            };
            _dbContext.LeaveLedgerEntries.Add(ledger);
            ledgerEntryId = ledger.Id;
        }

        // NFR-4: audit row for the override.
        _dbContext.LeaveApprovalHistories.Add(new LeaveApprovalHistory
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            LeaveRequestId = lopRequest.Id,
            ApproverEmployeeId = lopRequest.EmployeeId,
            ApprovalLevel = 1,
            Action = request.TargetLeaveTypeId is null ? LeaveApprovalAction.Cancelled : LeaveApprovalAction.Approved,
            Comment = request.TargetLeaveTypeId is null
                ? (request.Reason ?? "LOP removed by HR")
                : $"LOP converted by HR{(request.Reason is null ? string.Empty : $": {request.Reason}")}",
            ActionedAt = DateTime.UtcNow,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "LOP entry {LeaveRequestId} overridden ({Mode}) in tenant {TenantId}. Action {AuditAction}.",
            lopRequest.Id, request.TargetLeaveTypeId is null ? "removed" : "converted",
            _tenantContext.TenantId, "Leave.LopOverridden");

        return Result<OverrideLopResultDto>.Success(new OverrideLopResultDto
        {
            RequestId = lopRequest.Id,
            Status = lopRequest.Status.ToString(),
            IsLop = lopRequest.IsLop,
            LeaveTypeId = lopRequest.LeaveTypeId,
            LedgerEntryId = ledgerEntryId,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private LeaveRequest NewLopRequest(
        Guid employeeId, Guid lopTypeId, DateOnly date,
        LeaveRequestStatus status, LopSource source, string? reason) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantContext.TenantId,
        EmployeeId = employeeId,
        LeaveTypeId = lopTypeId,
        StartDate = date,
        EndDate = date,
        TotalDays = 1m,
        Reason = reason,
        Status = status,
        RequestedAt = DateTime.UtcNow,
        IsLop = true,
        LopSource = source,
    };

    /// <summary>
    /// Returns the subset of <paramref name="dates"/> on which the employee already has a non-cancelled
    /// LOP entry — the idempotency guard for assign/auto-generate.
    /// </summary>
    private async Task<HashSet<DateOnly>> ExistingLopDatesAsync(
        Guid employeeId, IReadOnlyList<DateOnly> dates, CancellationToken cancellationToken)
    {
        if (dates.Count == 0)
            return [];

        var min = dates.Min();
        var max = dates.Max();
        var dateSet = dates.ToHashSet();

        var existing = await _dbContext.LeaveRequests
            .AsNoTracking()
            .Where(lr => lr.IsLop
                         && lr.EmployeeId == employeeId
                         && lr.Status != LeaveRequestStatus.Cancelled
                         && lr.StartDate >= min && lr.StartDate <= max)
            .Select(lr => lr.StartDate)
            .ToListAsync(cancellationToken);

        return existing.Where(dateSet.Contains).ToHashSet();
    }

    private async Task<decimal> GetLedgerBalanceAsync(
        Guid employeeId, Guid leaveTypeId, int leaveYear, CancellationToken cancellationToken)
    {
        var lastEntry = await _dbContext.LeaveLedgerEntries
            .AsNoTracking()
            .Where(l => l.EmployeeId == employeeId && l.LeaveTypeId == leaveTypeId && l.LeaveYear == leaveYear)
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return lastEntry?.BalanceAfter ?? 0m;
    }
}
