using System.Text.Json;
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
    private readonly ICurrentUser _currentUser;
    private readonly ILeaveTypeService _leaveTypeService;
    private readonly IAttendanceProvider _attendanceProvider;
    private readonly ILeaveNotificationService _notificationService;
    private readonly ITenantLeaveYearResolver _leaveYearResolver;
    private readonly ILogger<LopService> _logger;

    // FR-6: page size for the compulsory-leave employee scan (mirrors LeaveAccrualJob's batching).
    private const int EmployeePageSize = 500;

    /// <summary>
    /// ISSUE-305: the LeaveLedger.LeaveYear label for <paramref name="date"/> under this tenant's leave year.
    /// Was a raw <c>.Year</c>, which reads a different ledger bucket than the accrual job writes for a fiscal
    /// tenant every Jan-Mar (balance 0 => this service would force LOP for employees who HAVE leave).
    /// </summary>
    private Task<int> LeaveYearForAsync(DateOnly date, CancellationToken cancellationToken)
        => _leaveYearResolver.LabelForAsync(date, cancellationToken);

    public LopService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILeaveTypeService leaveTypeService,
        IAttendanceProvider attendanceProvider,
        ILeaveNotificationService notificationService,
        ILogger<LopService> logger,
        // ISSUE-305: REQUIRED — see LeaveRequestService's ctor for why no `?? .Year` fallback.
        ITenantLeaveYearResolver leaveYearResolver)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _leaveTypeService = leaveTypeService;
        _attendanceProvider = attendanceProvider;
        _notificationService = notificationService;
        // ISSUE-305: REQUIRED (no null fallback) — so a missed wiring is a compile error, not a silent
        // re-key off the calendar year. (The ctor param above is non-nullable and non-defaulted.)
        _leaveYearResolver = leaveYearResolver;
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
        {
            // ISSUE-046 / NFR-4: emit a DISTINCT LOP-semantic audit action so the trail is queryable by
            // "LOP assigned" rather than only the generic LeaveRequest.Create rows the interceptor stamps.
            AddLopAudit("Leave.LopAssigned", request.EmployeeId, new
            {
                request.EmployeeId,
                Count = created.Count,
                Source = Domain.Enums.LopSource.HrAssigned.ToString(),
                request.Reason,
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

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

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<LopRegisterEntryDto>>> GetLopRegisterAsync(
        DateOnly from,
        DateOnly to,
        IReadOnlyList<Guid>? employeeIds = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<LopRegisterEntryDto>>.Failure("Tenant context is not resolved.", 400);
        if (to < from)
            return Result<IReadOnlyList<LopRegisterEntryDto>>.Failure("'to' must be on or after 'from'.", 400);

        var filterIds = employeeIds is { Count: > 0 } ? employeeIds.ToHashSet() : null;

        // Effective LOP entries overlapping the period, across all employees. Cancelled/Rejected are excluded.
        // The LeaveRequest global filter already scopes to tenant + not-deleted.
        var lopRequests = await _dbContext.LeaveRequests
            .AsNoTracking()
            .Where(lr => lr.IsLop
                         && lr.Status != LeaveRequestStatus.Cancelled
                         && lr.Status != LeaveRequestStatus.Rejected
                         && lr.StartDate <= to && lr.EndDate >= from
                         && (filterIds == null || filterIds.Contains(lr.EmployeeId)))
            .OrderBy(lr => lr.StartDate)
            .Select(lr => new
            {
                lr.Id,
                lr.EmployeeId,
                lr.StartDate,
                lr.TotalDays,
                lr.LopSource,
                lr.Status,
                lr.Reason,
            })
            .ToListAsync(cancellationToken);

        if (lopRequests.Count == 0)
            return Result<IReadOnlyList<LopRegisterEntryDto>>.Success([]);

        // Resolve employee identity via the employee's OWN query — NOT Include(lr => lr.Employee). Employee is a
        // required navigation with a global (tenant + soft-delete) filter, so an Include would emit an INNER JOIN
        // and silently drop rows whose employee is filtered out (THE INCLUDE TRAP — cost this repo 33 tests). This
        // separate, filtered lookup instead means a soft-deleted employee is excluded (its rows are omitted, not
        // dropped-with-siblings), while an employee whose OTHER principals (job title, etc.) are soft-deleted still
        // resolves because we never touch those navigations.
        var lookupIds = lopRequests.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => lookupIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeNo, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var entries = new List<LopRegisterEntryDto>(lopRequests.Count);
        foreach (var lr in lopRequests)
        {
            // Skip rows whose employee is not visible (soft-deleted / cross-tenant): identity can't be resolved.
            if (!employees.TryGetValue(lr.EmployeeId, out var emp))
                continue;

            entries.Add(new LopRegisterEntryDto
            {
                EmployeeId = lr.EmployeeId,
                EmployeeName = $"{emp.FirstName} {emp.LastName}".Trim(),
                EmployeeNo = emp.EmployeeNo,
                RequestId = lr.Id,
                Date = lr.StartDate,
                Days = lr.TotalDays,
                Source = lr.LopSource?.ToString() ?? string.Empty,
                Status = lr.Status.ToString(),
                Reason = lr.Reason,
            });
        }

        return Result<IReadOnlyList<LopRegisterEntryDto>>.Success(entries);
    }

    // ══════════════════════════════════════════════════════════════
    //  D2 / BUG-293: authoritative payroll LOP (leave owns the total)
    // ══════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, decimal>> GetPayrollLopDaysAsync(
        IReadOnlyCollection<Guid> employeeIds,
        DateOnly periodStart,
        DateOnly periodEnd,
        IReadOnlyDictionary<Guid, decimal> attendanceLopByEmployee,
        CancellationToken cancellationToken = default)
    {
        // Seed with attendance's raw facts (unapproved absence + lateness). Attendance OWNS those and the leave
        // module NEVER recomputes them — it only ADDS the paid/unpaid policy days below.
        var result = new Dictionary<Guid, decimal>(employeeIds.Count);
        foreach (var id in employeeIds)
            result[id] = attendanceLopByEmployee.TryGetValue(id, out var a) ? a : 0m;

        if (!_tenantContext.IsResolved || employeeIds.Count == 0)
            return result;

        var idSet = employeeIds as HashSet<Guid> ?? employeeIds.ToHashSet();

        // Approved-but-UNPAID leave overlapping the period. Status == Approved is LOAD-BEARING for disjointness:
        // it is EXACTLY the set AttendanceSummaryService excludes (an Approved leave day → LEAVE, lop += 0), so
        // attendance and this rail can never count the same day. HR-assigned / system-generated / compulsory LOP
        // (non-Approved statuses) are NOT counted here — attendance already deducts those absent days; wiring
        // them is D2's deferred second half (a real IAttendanceProvider). See ISSUE-357.
        var lopLeave = await _dbContext.LeaveRequests
            .AsNoTracking()
            .Where(lr => lr.IsLop
                         && idSet.Contains(lr.EmployeeId)
                         && lr.Status == LeaveRequestStatus.Approved
                         && lr.StartDate <= periodEnd && lr.EndDate >= periodStart)
            .Select(lr => new { lr.EmployeeId, lr.StartDate, lr.EndDate, lr.IsHalfDay })
            .ToListAsync(cancellationToken);

        foreach (var lr in lopLeave)
        {
            // Clip to the pay period and expand per-day, MIRRORING AttendanceSummaryService's leave expansion
            // (a single-day half-day request is 0.5; every other covered day is 1.0). Clipping is load-bearing:
            // a request straddling the month boundary contributes only its in-period days, never its whole span.
            var from = lr.StartDate < periodStart ? periodStart : lr.StartDate;
            var to = lr.EndDate > periodEnd ? periodEnd : lr.EndDate;
            decimal days = lr.IsHalfDay && lr.StartDate == lr.EndDate
                ? 0.5m
                : to.DayNumber - from.DayNumber + 1;
            result[lr.EmployeeId] = result.GetValueOrDefault(lr.EmployeeId) + days;
        }

        return result;
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
                    int leaveYear = await LeaveYearForAsync(date, cancellationToken);
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

                        // DF-19 / ISSUE-045: draw the balance down FIFO — carry-forward bucket first —
                        // and bump the bucket's ConsumedDays so the year-end expiry job accounts for the
                        // compulsory day instead of over-forfeiting it. Shared split; nets to −1.
                        await PooledLeaveLedger.AppendDeductionAsync(
                            _dbContext, _tenantContext.TenantId, employee.Id, request.LeaveTypeId, leaveYear,
                            totalDays: 1m, leaveReq.Id, balance,
                            $"Compulsory leave ({leaveType.Name}) on {date:yyyy-MM-dd}",
                            DateTime.UtcNow, LedgerEntryType.Used, cancellationToken);
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

        if (assignedCount > 0)
        {
            // ISSUE-046 / NFR-4: distinct semantic action for the compulsory-leave bulk assignment.
            AddLopAudit("Leave.CompulsoryAssigned", request.LeaveTypeId, new
            {
                request.LeaveTypeId,
                Dates = distinctDates.Count,
                Employees = employeesProcessed,
                Assigned = assignedCount,
                Lop = lopCount,
                request.Reason,
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
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
            // ISSUE-046 / NFR-4: the AUTO (absenteeism) LOP path is also an assignment that must be
            // queryable by the LOP-semantic action, not just the generic LeaveRequest.Create rows.
            AddLopAudit("Leave.LopAssigned", employeeId, new
            {
                EmployeeId = employeeId,
                Count = created,
                Source = Domain.Enums.LopSource.SystemGenerated.ToString(),
                Reason = "Unaccounted absence",
            });
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
            int leaveYear = await LeaveYearForAsync(lopRequest.StartDate, cancellationToken);
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

            // DF-19 / ISSUE-045: draw the balance down FIFO — carry-forward bucket first — and bump the
            // bucket's ConsumedDays so the year-end expiry job accounts for the converted days instead of
            // over-forfeiting them. Shared split; nets to −TotalDays, final BalanceAfter == projected.
            var deduction = await PooledLeaveLedger.AppendDeductionAsync(
                _dbContext, _tenantContext.TenantId, lopRequest.EmployeeId, targetType.Id, leaveYear,
                lopRequest.TotalDays, lopRequest.Id, balance,
                $"LOP converted to {targetType.Name}: {lopRequest.TotalDays} day(s)",
                DateTime.UtcNow, LedgerEntryType.Used, cancellationToken);
            ledgerEntryId = deduction.FinalRow.Id;
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

        // ISSUE-046 / NFR-4: distinct semantic action for the HR override (remove/convert) of an LOP entry.
        AddLopAudit("Leave.LopOverridden", lopRequest.Id, new
        {
            LeaveRequestId = lopRequest.Id,
            lopRequest.EmployeeId,
            Mode = request.TargetLeaveTypeId is null ? "removed" : "converted",
            request.TargetLeaveTypeId,
            request.Reason,
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

    /// <summary>
    /// ISSUE-046 / NFR-4: adds a DISTINCT LOP-semantic audit row (Leave.LopAssigned /
    /// Leave.CompulsoryAssigned / Leave.LopOverridden) to the change set WITHOUT saving — the caller's
    /// SaveChanges commits it in the same save. The generic AuditCaptureInterceptor still stamps the
    /// per-row LeaveRequest.Create/Update entries; this adds the queryable business-action trail on top.
    /// IP/UserAgent are enriched onto this AuditLog row by AuditInterceptor.
    /// </summary>
    private void AddLopAudit(string action, Guid resourceId, object detail)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "LeaveRequest",
            ResourceId = resourceId.ToString(),
            After = JsonSerializer.Serialize(detail),
            CreatedAt = DateTime.UtcNow,
        });
    }
}
