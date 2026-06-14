using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using HRM.Application.Features.LeaveRequests;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Employee leave-request submission and retrieval (US-LV-003).
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// Balance is read from the LeaveLedger running total (reuses US-LV-002's source).
/// </summary>
public sealed class LeaveRequestService : ILeaveRequestService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IHolidayProvider _holidayProvider;
    private readonly IHolidayService _holidayService;
    private readonly ILeaveNotificationService _notificationService;
    private readonly ILeaveTypeService? _leaveTypeService;
    private readonly ILogger<LeaveRequestService> _logger;

    // BR-1 / BR-2 configurable defaults. No tenant-settings entity exists yet, so these
    // act as constants. TODO(tenant-settings): read lookback/future window from tenant settings.
    private const int DefaultPastLookbackDays = 7;   // BR-1
    private const int DefaultFutureWindowDays = 90;  // BR-2

    // US-LV-010 FR-7: tenant-configurable "allow cancellation up to N days before start date".
    // Default 0 = an approved leave can be cancelled any time BEFORE it starts, but is blocked once
    // start_date <= today (BR-3, AC-3). No tenant-settings entity exists yet, so this is a constant.
    // TODO(tenant-settings): read the cancellation window from tenant settings when available.
    private const int DefaultCancellationWindowDays = 0;

    public LeaveRequestService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IHolidayProvider holidayProvider,
        ILeaveNotificationService notificationService,
        ILogger<LeaveRequestService> logger,
        IHolidayService? holidayService = null,
        ILeaveTypeService? leaveTypeService = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _holidayProvider = holidayProvider;
        // US-LV-009: optional so the existing US-LV-003/004/005 unit tests (which construct this
        // service with the original 6 args) keep compiling. The DI container always supplies it,
        // so production always has holiday data for the team calendar (FR-7).
        _holidayService = holidayService!;
        // US-LV-011: optional for the same back-compat reason. DI always supplies it; it is only
        // needed for the AC-1 confirm-LOP path (resolving the system LOP leave type).
        _leaveTypeService = leaveTypeService;
        _notificationService = notificationService;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Create (FR-5/FR-6, AC-1..AC-6, BR-1..BR-6)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<LeaveRequestDto>> CreateAsync(
        CreateLeaveRequestRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveRequestDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<LeaveRequestDto>.Failure("No employee record is linked to the current user.", 403);

        var leaveType = await _dbContext.LeaveTypes
            .FirstOrDefaultAsync(lt => lt.Id == request.LeaveTypeId, cancellationToken);
        if (leaveType is null)
            return Result<LeaveRequestDto>.Failure("Leave type not found.", 404);
        if (!leaveType.IsActive)
            return Result<LeaveRequestDto>.Failure("This leave type is no longer active.", 400);

        // BR-4: Gender-restricted leave types.
        if (leaveType.Gender != LeaveTypeGender.All)
        {
            if (!EmployeeMatchesLeaveTypeGender(employee.Gender, leaveType.Gender))
                return Result<LeaveRequestDto>.Failure(
                    "This leave type is not available for your profile.", 400);
        }

        // BR-5: Probation eligibility.
        if (employee.Status == EmployeeStatus.Probation && !leaveType.ProbationEligible)
            return Result<LeaveRequestDto>.Failure(
                "This leave type is not available during probation.", 400);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // BR-1: Past-date lookback window.
        var earliest = today.AddDays(-DefaultPastLookbackDays);
        if (request.StartDate < earliest)
            return Result<LeaveRequestDto>.Failure(
                $"Leave cannot be requested for dates more than {DefaultPastLookbackDays} days in the past.", 400);

        // BR-2: Future window.
        var latest = today.AddDays(DefaultFutureWindowDays);
        if (request.StartDate > latest || request.EndDate > latest)
            return Result<LeaveRequestDto>.Failure(
                $"Leave cannot be requested for dates more than {DefaultFutureWindowDays} days in the future.", 400);

        // Half-day support gate (AC-4).
        if (request.IsHalfDay && !leaveType.HalfDayAllowed)
            return Result<LeaveRequestDto>.Failure(
                "Half-day leave is not allowed for this leave type.", 400);

        // FR-3 / AC-6: Compute working days, excluding weekends and (when available) holidays.
        decimal totalDays;
        if (request.IsHalfDay)
        {
            // AC-4: half-day = 0.5 day on a single working day.
            if (!WorkingDaysCalculator.DefaultWorkWeek.Contains(request.StartDate.DayOfWeek))
                return Result<LeaveRequestDto>.Failure(
                    "A half-day leave cannot fall on a non-working day.", 400);
            totalDays = 0.5m;
        }
        else
        {
            // US-LV-007 AC-2/BR-2: exclude public holidays, scoped to the employee's location.
            var holidays = await _holidayProvider.GetHolidaysAsync(
                request.StartDate, request.EndDate, employee.LocationId, cancellationToken);
            totalDays = WorkingDaysCalculator.CountWorkingDays(
                request.StartDate, request.EndDate, holidays: holidays);
        }

        if (totalDays <= 0m)
            return Result<LeaveRequestDto>.Failure(
                "The selected date range contains no working days.", 400);

        // BR-3: Max consecutive days per leave type.
        if (leaveType.MaxConsecutiveDays.HasValue && totalDays > leaveType.MaxConsecutiveDays.Value)
            return Result<LeaveRequestDto>.Failure(
                $"This leave type allows a maximum of {leaveType.MaxConsecutiveDays.Value} consecutive days.", 400);

        // AC-3: Document requirement.
        var attachments = request.Attachments?.Where(a => !string.IsNullOrWhiteSpace(a)).ToList() ?? [];
        if (leaveType.DocumentsRequired)
        {
            int threshold = leaveType.DocumentDayThreshold ?? 0;
            if (totalDays > threshold && attachments.Count == 0)
                return Result<LeaveRequestDto>.Failure(
                    $"Medical certificate is required for {leaveType.Name.ToLowerInvariant()} exceeding {threshold} days.", 400);
        }

        // AC-5 / FR-4: Overlap with an existing Pending/Approved request for the same employee.
        bool overlaps = await _dbContext.LeaveRequests.AnyAsync(lr =>
            lr.EmployeeId == employee.Id &&
            (lr.Status == LeaveRequestStatus.Pending || lr.Status == LeaveRequestStatus.Approved) &&
            lr.StartDate <= request.EndDate &&
            lr.EndDate >= request.StartDate,
            cancellationToken);
        if (overlaps)
            return Result<LeaveRequestDto>.Failure(
                "You already have a leave request for the selected dates.", 409);

        // AC-2: Balance check from the ledger running total.
        int leaveYear = request.StartDate.Year;
        decimal currentBalance = await GetLedgerBalanceAsync(
            employee.Id, leaveType.Id, leaveYear, cancellationToken);

        // US-LV-011 AC-1: when balance is insufficient and negative balance is NOT allowed, the request
        // can be processed as Loss of Pay (LOP). If the employee confirmed LOP, create the request as an
        // LOP entry (is_lop = true, lop_source = EmployeeRequest, no balance deduction — BR-1); otherwise
        // keep the existing hard block, but surface a message that signals the LOP option to the FE.
        bool createAsLop = false;
        decimal projected = currentBalance - totalDays;
        if (projected < 0m)
        {
            if (!leaveType.NegativeBalanceAllowed)
            {
                if (request.ConfirmLop)
                {
                    createAsLop = true;
                }
                else
                {
                    return Result<LeaveRequestDto>.Failure(
                        $"Insufficient leave balance. Available: {currentBalance} day(s), requested: {totalDays} day(s). " +
                        "This can be processed as Loss of Pay (LOP).", 400);
                }
            }
            else if (leaveType.NegativeBalanceLimit.HasValue && -projected > leaveType.NegativeBalanceLimit.Value)
            {
                return Result<LeaveRequestDto>.Failure(
                    $"Request exceeds the allowed negative balance limit of {leaveType.NegativeBalanceLimit.Value} day(s).", 400);
            }
        }

        // AC-1: when confirmed as LOP, the request is recorded against the system LOP leave type (FR-1)
        // with is_lop = true and NO balance deduction. The original leave type is captured in the reason
        // so HR/payroll keep the context.
        Guid effectiveLeaveTypeId = leaveType.Id;
        string effectiveLeaveTypeName = leaveType.Name;
        LopSource? lopSource = null;
        if (createAsLop)
        {
            if (_leaveTypeService is null)
                return Result<LeaveRequestDto>.Failure(
                    "Loss of Pay processing is not available.", 400);

            var lopTypeResult = await _leaveTypeService.EnsureLopTypeForTenantAsync(
                _tenantContext.TenantId, cancellationToken);
            if (lopTypeResult.IsFailure)
                return Result<LeaveRequestDto>.Failure(lopTypeResult.Error!, lopTypeResult.StatusCode ?? 400);

            effectiveLeaveTypeId = lopTypeResult.Value;
            effectiveLeaveTypeName = "Loss of Pay";
            lopSource = LopSource.EmployeeRequest;
        }

        var leaveRequest = new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employee.Id,
            LeaveTypeId = effectiveLeaveTypeId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsHalfDay = request.IsHalfDay,
            HalfDaySession = request.IsHalfDay ? request.HalfDaySession?.ToUpperInvariant() : null,
            TotalDays = totalDays,
            Reason = createAsLop && request.Reason is null
                ? $"Loss of Pay (insufficient {leaveType.Name} balance)"
                : request.Reason,
            Status = LeaveRequestStatus.Pending,
            RequestedAt = DateTime.UtcNow,
            AttachmentUrls = attachments,
            IsLop = createAsLop,
            LopSource = lopSource,
        };

        _dbContext.LeaveRequests.Add(leaveRequest);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created leave request {LeaveRequestId} for employee {EmployeeId} ({LeaveTypeId}, {TotalDays} days, {Start}-{End}) in tenant {TenantId}",
            leaveRequest.Id, employee.Id, leaveType.Id, totalDays,
            request.StartDate, request.EndDate, _tenantContext.TenantId);

        // FR-6 / AC-1: notify the reporting manager (seam — see ILeaveNotificationService).
        // TODO(notifications): replace the log-only implementation with the real service (US-LV-004+).
        await _notificationService.NotifyLeaveRequestedAsync(
            leaveRequest.Id, employee.Id, employee.ReportsToEmployeeId, cancellationToken);

        return Result<LeaveRequestDto>.Success(MapToDto(leaveRequest, effectiveLeaveTypeName));
    }

    // ══════════════════════════════════════════════════════════════
    //  Queries
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<LeaveRequestDto>>> GetMineAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<LeaveRequestDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<LeaveRequestDto>>.Failure(
                "No employee record is linked to the current user.", 403);

        var requests = await _dbContext.LeaveRequests
            .AsNoTracking()
            .Include(lr => lr.LeaveType)
            .Where(lr => lr.EmployeeId == employee.Id)
            .OrderByDescending(lr => lr.RequestedAt)
            .ToListAsync(cancellationToken);

        var dtos = requests.Select(lr => MapToDto(lr, lr.LeaveType?.Name ?? string.Empty)).ToList();
        return Result<IReadOnlyList<LeaveRequestDto>>.Success(dtos);
    }

    public async Task<Result<LeaveBalancePreviewDto>> GetBalancePreviewAsync(
        Guid leaveTypeId,
        DateOnly? startDate,
        DateOnly? endDate,
        bool isHalfDay,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveBalancePreviewDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<LeaveBalancePreviewDto>.Failure(
                "No employee record is linked to the current user.", 403);

        var leaveType = await _dbContext.LeaveTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(lt => lt.Id == leaveTypeId, cancellationToken);
        if (leaveType is null)
            return Result<LeaveBalancePreviewDto>.Failure("Leave type not found.", 404);

        int leaveYear = (startDate ?? DateOnly.FromDateTime(DateTime.UtcNow)).Year;
        decimal currentBalance = await GetLedgerBalanceAsync(
            employee.Id, leaveTypeId, leaveYear, cancellationToken);

        decimal requestedDays = 0m;
        if (startDate.HasValue && endDate.HasValue && endDate.Value >= startDate.Value)
        {
            if (isHalfDay)
            {
                requestedDays = 0.5m;
            }
            else
            {
                // US-LV-007 AC-2/BR-2: exclude public holidays, scoped to the employee's location.
                var holidays = await _holidayProvider.GetHolidaysAsync(
                    startDate.Value, endDate.Value, employee.LocationId, cancellationToken);
                requestedDays = WorkingDaysCalculator.CountWorkingDays(
                    startDate.Value, endDate.Value, holidays: holidays);
            }
        }

        return Result<LeaveBalancePreviewDto>.Success(new LeaveBalancePreviewDto
        {
            EmployeeId = employee.Id,
            LeaveTypeId = leaveTypeId,
            LeaveTypeName = leaveType.Name,
            LeaveYear = leaveYear,
            CurrentBalance = currentBalance,
            RequestedDays = requestedDays,
            ProjectedBalance = currentBalance - requestedDays,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Manager pending queue (US-LV-004 FR-1..FR-5, BR-1/BR-3)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PendingLeaveQueueResult>> GetPendingForManagerAsync(
        PendingLeaveQueueQueryParams queryParams,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PendingLeaveQueueResult>.Failure("Tenant context is not resolved.", 400);

        // §10: cap page size at 50, default 20; page is 1-based.
        int pageSize = Math.Clamp(queryParams.PageSize <= 0 ? 20 : queryParams.PageSize, 1, 50);
        int page = queryParams.Page <= 0 ? 1 : queryParams.Page;

        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null)
        {
            // No employee record linked to the current user -> empty queue (not an error).
            return Result<PendingLeaveQueueResult>.Success(new PendingLeaveQueueResult
            {
                Items = [],
                TotalCount = 0,
                Page = page,
                PageSize = pageSize,
            });
        }

        // BR-1 / NFR-3: direct reports only. ReportsToEmployeeId is the manager FK on Employee.
        // Both the employee and the leave request are tenant-scoped by the global query filter.
        // TODO(multi-level): BR-2 multi-level approval routing is out of scope — only single-level
        //   direct-report scope is implemented. Add level-based queues when approval levels exist.
        var teamMemberIds = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.ReportsToEmployeeId == manager.Id)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        if (teamMemberIds.Count == 0)
        {
            return Result<PendingLeaveQueueResult>.Success(new PendingLeaveQueueResult
            {
                Items = [],
                TotalCount = 0,
                Page = page,
                PageSize = pageSize,
            });
        }

        var teamMemberIdSet = teamMemberIds.ToHashSet();

        // Base query: pending requests from the manager's direct reports (uses ix_leave_pending).
        var pendingQuery = _dbContext.LeaveRequests
            .AsNoTracking()
            .Include(lr => lr.Employee)
            .Include(lr => lr.LeaveType)
            .Where(lr => lr.Status == LeaveRequestStatus.Pending
                         && teamMemberIdSet.Contains(lr.EmployeeId));

        // FR-3 filters.
        if (queryParams.LeaveTypeId.HasValue)
            pendingQuery = pendingQuery.Where(lr => lr.LeaveTypeId == queryParams.LeaveTypeId.Value);

        if (queryParams.EmployeeId.HasValue)
            pendingQuery = pendingQuery.Where(lr => lr.EmployeeId == queryParams.EmployeeId.Value);

        // Date-range window: overlap with [StartDate, EndDate] when supplied.
        if (queryParams.StartDate.HasValue)
            pendingQuery = pendingQuery.Where(lr => lr.EndDate >= queryParams.StartDate.Value);
        if (queryParams.EndDate.HasValue)
            pendingQuery = pendingQuery.Where(lr => lr.StartDate <= queryParams.EndDate.Value);

        int totalCount = await pendingQuery.CountAsync(cancellationToken);

        // FR-3 sort: requestedAt (default, AC-1 oldest first) or startDate.
        bool sortByStart = string.Equals(queryParams.SortBy, "startDate", StringComparison.OrdinalIgnoreCase);
        pendingQuery = (sortByStart, queryParams.SortAscending) switch
        {
            (true, true) => pendingQuery.OrderBy(lr => lr.StartDate).ThenBy(lr => lr.RequestedAt),
            (true, false) => pendingQuery.OrderByDescending(lr => lr.StartDate).ThenByDescending(lr => lr.RequestedAt),
            (false, true) => pendingQuery.OrderBy(lr => lr.RequestedAt),
            (false, false) => pendingQuery.OrderByDescending(lr => lr.RequestedAt),
        };

        // FR-4 pagination.
        var pageRequests = await pendingQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // FR-5: approved leaves of the whole team in the relevant window, for conflict counting.
        // Only load what is needed to overlap-check the requests on this page.
        var approvedTeamLeaves = pageRequests.Count == 0
            ? new List<(Guid EmployeeId, DateOnly Start, DateOnly End)>()
            : (await _dbContext.LeaveRequests
                .AsNoTracking()
                .Where(lr => lr.Status == LeaveRequestStatus.Approved
                             && teamMemberIdSet.Contains(lr.EmployeeId))
                .Select(lr => new { lr.EmployeeId, lr.StartDate, lr.EndDate })
                .ToListAsync(cancellationToken))
                .Select(x => (EmployeeId: x.EmployeeId, Start: x.StartDate, End: x.EndDate))
                .ToList();

        var now = DateTime.UtcNow;
        var items = new List<PendingLeaveRequestDto>(pageRequests.Count);

        foreach (var lr in pageRequests)
        {
            // BR-4: real-time balance from the ledger running total (same source as US-LV-003).
            // NFR-2: the story specifies a Redis-cached balance with DB fallback. Redis caching is
            //   DEFERRED across the leave module (vault decision); the ledger total is the single
            //   source of truth. TODO(redis-balance-cache): wrap GetLedgerBalanceAsync in an
            //   IDistributedCache layer keyed tenant:{tenantId}:leave_balance:{empId}:{leaveTypeId}.
            decimal balance = await GetLedgerBalanceAsync(
                lr.EmployeeId, lr.LeaveTypeId, lr.StartDate.Year, cancellationToken);

            // FR-5: count team-mates (excluding the requester) with an Approved leave overlapping
            //   this request's [StartDate, EndDate].
            int conflicts = approvedTeamLeaves.Count(a =>
                a.EmployeeId != lr.EmployeeId &&
                a.Start <= lr.EndDate &&
                a.End >= lr.StartDate);

            items.Add(new PendingLeaveRequestDto
            {
                RequestId = lr.Id,
                EmployeeId = lr.EmployeeId,
                EmployeeName = lr.Employee is null
                    ? string.Empty
                    : $"{lr.Employee.FirstName} {lr.Employee.LastName}".Trim(),
                EmployeePhoto = lr.Employee?.ProfilePhotoUrl,
                LeaveTypeId = lr.LeaveTypeId,
                LeaveTypeName = lr.LeaveType?.Name ?? string.Empty,
                LeaveTypeColor = lr.LeaveType?.Color,
                StartDate = lr.StartDate,
                EndDate = lr.EndDate,
                TotalDays = lr.TotalDays,
                Reason = lr.Reason,
                HasAttachments = lr.AttachmentUrls.Count > 0,
                CurrentBalance = balance,
                RequestedAt = lr.RequestedAt,
                IsOverdue = (now - lr.RequestedAt).TotalDays > 30,  // BR-3
                TeamConflictCount = conflicts,
            });
        }

        // FR-6 / AC-5: real-time SignalR push when a new request arrives is DEFERRED — no
        //   notification hub exists yet (§10 references /hubs/notifications). The manager-notify
        //   target is the log-only ILeaveNotificationService seam from US-LV-003. AC-5 is met at
        //   the API level: this query returns fresh data on every reload.
        //   TODO(notifications/SignalR): push queue updates to the manager via /hubs/notifications.

        return Result<PendingLeaveQueueResult>.Success(new PendingLeaveQueueResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Team leave calendar (US-LV-009 FR-1..FR-4, FR-6/FR-7, BR-1..BR-5)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<TeamLeaveCalendarDto>> GetTeamLeaveCalendarAsync(
        TeamLeaveCalendarQueryParams queryParams,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<TeamLeaveCalendarDto>.Failure("Tenant context is not resolved.", 400);

        if (queryParams.To < queryParams.From)
            return Result<TeamLeaveCalendarDto>.Failure("'to' must be on or after 'from'.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
        {
            // No employee record linked to the current user -> empty calendar (not an error),
            // mirroring the pending-queue behaviour (US-LV-004).
            return Result<TeamLeaveCalendarDto>.Success(EmptyCalendar(queryParams, TeamCalendarScope.Employee));
        }

        // ── Resolve the caller's scope (AC-1/AC-2, BR-1/BR-2/BR-3) ──
        // BR-3: HR Officers with Leave.View.All see the whole organisation.
        bool canViewAll = _currentUser.Permissions.Contains(PermissionCatalog.Leave.ViewAll);

        TeamCalendarScope scope;
        IReadOnlyCollection<Guid>? employeeIdFilter; // null = no employee restriction (HR sees all)

        if (canViewAll)
        {
            scope = TeamCalendarScope.All;
            employeeIdFilter = null;
        }
        else
        {
            // Manager scope: this employee has direct reports (BR-2).
            var directReportIds = await _dbContext.Employees
                .AsNoTracking()
                .Where(e => e.ReportsToEmployeeId == employee.Id)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);

            if (directReportIds.Count > 0)
            {
                scope = TeamCalendarScope.Manager;
                employeeIdFilter = directReportIds;
            }
            else
            {
                // Employee scope: own-department colleagues only (BR-1).
                var deptColleagueIds = await _dbContext.Employees
                    .AsNoTracking()
                    .Where(e => e.DepartmentId == employee.DepartmentId)
                    .Select(e => e.Id)
                    .ToListAsync(cancellationToken);

                scope = TeamCalendarScope.Employee;
                employeeIdFilter = deptColleagueIds;
            }
        }

        bool fullDetail = scope != TeamCalendarScope.Employee;

        // ── Leave-request query (tenant-scoped by the global query filter) ──
        // BR-4: Cancelled excluded. Rejected also excluded — only Approved (+ Pending where allowed).
        // Employee scope (BR-1): Approved only, never Pending.
        var leaveQuery = _dbContext.LeaveRequests
            .AsNoTracking()
            .Include(lr => lr.Employee)
            .Include(lr => lr.LeaveType)
            .Where(lr => lr.StartDate <= queryParams.To && lr.EndDate >= queryParams.From);

        leaveQuery = fullDetail
            ? leaveQuery.Where(lr => lr.Status == LeaveRequestStatus.Approved
                                     || lr.Status == LeaveRequestStatus.Pending)
            : leaveQuery.Where(lr => lr.Status == LeaveRequestStatus.Approved);

        // Scope restriction. HR scope (employeeIdFilter == null) has no restriction.
        if (employeeIdFilter is not null)
        {
            var idSet = employeeIdFilter as ICollection<Guid> ?? employeeIdFilter.ToList();
            if (idSet.Count == 0)
            {
                return Result<TeamLeaveCalendarDto>.Success(
                    await BuildCalendarAsync(queryParams, scope, [], employee.LocationId, cancellationToken));
            }
            leaveQuery = leaveQuery.Where(lr => idSet.Contains(lr.EmployeeId));
        }

        // FR-6 filters (employee + leave type apply to all scopes; status is manager/HR only).
        if (queryParams.EmployeeId.HasValue)
            leaveQuery = leaveQuery.Where(lr => lr.EmployeeId == queryParams.EmployeeId.Value);

        if (fullDetail && queryParams.LeaveTypeId.HasValue)
            leaveQuery = leaveQuery.Where(lr => lr.LeaveTypeId == queryParams.LeaveTypeId.Value);

        if (fullDetail && !string.IsNullOrWhiteSpace(queryParams.Status)
            && Enum.TryParse<LeaveRequestStatus>(queryParams.Status, ignoreCase: true, out var statusFilter)
            && (statusFilter == LeaveRequestStatus.Approved || statusFilter == LeaveRequestStatus.Pending))
        {
            leaveQuery = leaveQuery.Where(lr => lr.Status == statusFilter);
        }

        var leaves = await leaveQuery
            .OrderBy(lr => lr.StartDate)
            .ToListAsync(cancellationToken);

        var entries = leaves.Select(lr => new TeamLeaveCalendarEntryDto
        {
            EmployeeId = lr.EmployeeId,
            EmployeeName = lr.Employee is null
                ? string.Empty
                : $"{lr.Employee.FirstName} {lr.Employee.LastName}".Trim(),
            // BR-1: employee scope must NOT leak leave type / colour / pending-vs-approved status.
            LeaveTypeName = fullDetail ? (lr.LeaveType?.Name ?? string.Empty) : null,
            Color = fullDetail ? lr.LeaveType?.Color : null,
            Status = fullDetail ? lr.Status.ToString() : null,
            StartDate = lr.StartDate,
            EndDate = lr.EndDate,
            TotalDays = lr.TotalDays,
            IsHalfDay = lr.IsHalfDay,          // BR-5 half-day passthrough
            HalfDaySession = lr.HalfDaySession,
        }).ToList();

        return Result<TeamLeaveCalendarDto>.Success(
            await BuildCalendarAsync(queryParams, scope, entries, employee.LocationId, cancellationToken));
    }

    private enum TeamCalendarScope { All, Manager, Employee }

    private TeamLeaveCalendarDto EmptyCalendar(TeamLeaveCalendarQueryParams p, TeamCalendarScope scope) => new()
    {
        From = p.From,
        To = p.To,
        Scope = scope.ToString(),
        Entries = [],
        Holidays = [],
    };

    /// <summary>
    /// Assembles the calendar payload, fetching the public holidays in the range (FR-7) via the
    /// US-LV-007 holiday service (reuse). Holidays are scoped to the caller's location so a
    /// location-specific holiday does not appear for everyone (consistent with US-LV-007 BR-2).
    /// </summary>
    private async Task<TeamLeaveCalendarDto> BuildCalendarAsync(
        TeamLeaveCalendarQueryParams p,
        TeamCalendarScope scope,
        IReadOnlyList<TeamLeaveCalendarEntryDto> entries,
        Guid? locationId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<HolidayDto> holidays = [];
        if (_holidayService is not null)
        {
            // FR-7: public holidays in the range for background highlights. GetAllAsync returns
            // active holidays in the inclusive [from, to] range; we keep Public ones (the type
            // shown as background highlights, matching the holiday-exclusion rule in US-LV-007).
            var holidayResult = await _holidayService.GetAllAsync(
                from: p.From, to: p.To, year: null, locationId: locationId,
                activeOnly: true, cancellationToken: cancellationToken);

            if (holidayResult.IsSuccess && holidayResult.Value is not null)
            {
                holidays = holidayResult.Value
                    .Where(h => h.LocationId == null || h.LocationId == locationId)
                    .Where(h => string.Equals(h.Type, "Public", StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        return new TeamLeaveCalendarDto
        {
            From = p.From,
            To = p.To,
            Scope = scope.ToString(),
            Entries = entries,
            Holidays = holidays,
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Approve / Reject (US-LV-005 FR-1..FR-7, AC-1..AC-3/AC-5, BR-1..BR-5)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<LeaveApprovalResultDto>> ApproveAsync(
        Guid leaveRequestId,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var ctx = await LoadForDecisionAsync(leaveRequestId, cancellationToken);
        if (ctx.Failure is not null)
            return ctx.Failure;

        var request = ctx.Request!;
        var leaveType = ctx.LeaveType!;
        var manager = ctx.Manager!;

        // BR-4: payroll-lock check is DEFERRED — no payroll module exists yet.
        // TODO(payroll): block approval when the request period falls in a payroll-locked period
        //   ("Cannot approve leave for a payroll-locked period.").

        // BR-5 / AC-3: recompute balance at approval time (not at request time). If insufficient
        // and the leave type does NOT allow a negative balance -> block (400). If negative is
        // allowed, the API approves (the manager "confirmation" modal is a UI concern, AC-3).
        int leaveYear = request.StartDate.Year;
        decimal currentBalance = await GetLedgerBalanceAsync(
            request.EmployeeId, leaveType.Id, leaveYear, cancellationToken);

        decimal projected = currentBalance - request.TotalDays;
        if (projected < 0m && !leaveType.NegativeBalanceAllowed)
        {
            return Result<LeaveApprovalResultDto>.Failure(
                $"Insufficient leave balance to approve. Available: {currentBalance} day(s), requested: {request.TotalDays} day(s).",
                400);
        }

        // FR-3 / AC-1: append a LeaveLedger "Used" deduction, linked to the request, computing
        // balance_after from the running total (reuses the US-LV-002 accrual ledger-append logic).
        var ledgerEntry = new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EntryType = LedgerEntryType.Used,
            EmployeeId = request.EmployeeId,
            LeaveTypeId = leaveType.Id,
            LeaveYear = leaveYear,
            LeaveRequestId = request.Id,
            Amount = -request.TotalDays,
            BalanceAfter = projected,
            Description = $"Leave approved ({leaveType.Name}): {request.TotalDays} day(s)",
            OccurredAt = DateTime.UtcNow,
        };

        request.Status = LeaveRequestStatus.Approved;

        var history = NewHistory(request.Id, manager.Id, LeaveApprovalAction.Approved, comment);

        _dbContext.LeaveLedgerEntries.Add(ledgerEntry);
        _dbContext.LeaveApprovalHistories.Add(history);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // AC-5 / FR-6 / NFR-4: another approver actioned this request first (xmin token).
            return Result<LeaveApprovalResultDto>.Failure(
                "This request has already been actioned.", 409);
        }

        _logger.LogInformation(
            "Leave request {LeaveRequestId} approved by manager {ManagerEmployeeId} for employee {EmployeeId}: " +
            "ledger {LedgerEntryId}, balance {BalanceAfter} in tenant {TenantId}. Action {AuditAction} resource {Resource}.",
            request.Id, manager.Id, request.EmployeeId, ledgerEntry.Id, projected, _tenantContext.TenantId,
            "Leave.Approved", "LeaveRequest");

        // FR-3 / AC-1: invalidate the Redis balance cache. Redis caching is DEFERRED module-wide
        // (vault decision); the ledger total is the single source of truth.
        // TODO(redis-balance-cache): invalidate key tenant:{tenantId}:leave_balance:{empId}:{leaveTypeId}.

        // NFR-2 / AC-1: queue a "leave-approved" notification (fire-and-forget log-only seam).
        await _notificationService.NotifyLeaveApprovedAsync(
            request.Id, request.EmployeeId, manager.Id, cancellationToken);

        return Result<LeaveApprovalResultDto>.Success(new LeaveApprovalResultDto
        {
            RequestId = request.Id,
            Status = request.Status.ToString(),
            Action = LeaveApprovalAction.Approved.ToString(),
            ApprovalLevel = history.ApprovalLevel,
            LedgerEntryId = ledgerEntry.Id,
            BalanceAfter = projected,
            ActionedAt = history.ActionedAt,
        });
    }

    public async Task<Result<LeaveApprovalResultDto>> RejectAsync(
        Guid leaveRequestId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        // BR-2: reason is mandatory (also enforced by the validator; guard defensively here).
        if (string.IsNullOrWhiteSpace(reason))
            return Result<LeaveApprovalResultDto>.Failure("A rejection reason is required.", 400);

        var ctx = await LoadForDecisionAsync(leaveRequestId, cancellationToken);
        if (ctx.Failure is not null)
            return ctx.Failure;

        var request = ctx.Request!;
        var manager = ctx.Manager!;

        // FR-4: no ledger entry on rejection — only a status update and approval-history record.
        request.Status = LeaveRequestStatus.Rejected;

        var history = NewHistory(request.Id, manager.Id, LeaveApprovalAction.Rejected, reason);
        _dbContext.LeaveApprovalHistories.Add(history);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // AC-5: another approver actioned this request first.
            return Result<LeaveApprovalResultDto>.Failure(
                "This request has already been actioned.", 409);
        }

        _logger.LogInformation(
            "Leave request {LeaveRequestId} rejected by manager {ManagerEmployeeId} for employee {EmployeeId} " +
            "in tenant {TenantId}. Action {AuditAction} resource {Resource}.",
            request.Id, manager.Id, request.EmployeeId, _tenantContext.TenantId, "Leave.Rejected", "LeaveRequest");

        // NFR-2 / AC-2: queue a "leave-rejected" notification with the reason (log-only seam).
        await _notificationService.NotifyLeaveRejectedAsync(
            request.Id, request.EmployeeId, manager.Id, reason, cancellationToken);

        return Result<LeaveApprovalResultDto>.Success(new LeaveApprovalResultDto
        {
            RequestId = request.Id,
            Status = request.Status.ToString(),
            Action = LeaveApprovalAction.Rejected.ToString(),
            ApprovalLevel = history.ApprovalLevel,
            LedgerEntryId = null,
            BalanceAfter = null,
            ActionedAt = history.ActionedAt,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Cancel (US-LV-010 FR-1..FR-7, AC-1..AC-4, BR-1..BR-5)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<LeaveCancellationResultDto>> CancelAsync(
        Guid leaveRequestId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveCancellationResultDto>.Failure("Tenant context is not resolved.", 400);

        // BR-1: resolve the acting employee. Only the requester may cancel their OWN leave.
        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<LeaveCancellationResultDto>.Failure(
                "No employee record is linked to the current user.", 403);

        // Tracked load (we mutate Status). Tenant-scoped by the global query filter.
        var request = await _dbContext.LeaveRequests
            .FirstOrDefaultAsync(lr => lr.Id == leaveRequestId, cancellationToken);
        if (request is null)
            return Result<LeaveCancellationResultDto>.Failure("Leave request not found.", 404);

        // BR-1: ownership — only the requesting employee may cancel. A manager cancelling on
        // behalf is forbidden (they can reject a pending request instead). 403, never 404, so we
        // do not leak whether the (tenant-visible) request exists vs. is simply not theirs.
        if (request.EmployeeId != employee.Id)
            return Result<LeaveCancellationResultDto>.Failure(
                "You can only cancel your own leave requests.", 403);

        // BR-2: only Pending or Approved requests can be cancelled. Rejected/already-Cancelled
        // cannot be cancelled again.
        if (request.Status == LeaveRequestStatus.Cancelled)
            return Result<LeaveCancellationResultDto>.Failure(
                "This leave request has already been cancelled.", 409);
        if (request.Status != LeaveRequestStatus.Pending && request.Status != LeaveRequestStatus.Approved)
            return Result<LeaveCancellationResultDto>.Failure(
                $"A {request.Status.ToString().ToLowerInvariant()} leave request cannot be cancelled.", 400);

        bool wasApproved = request.Status == LeaveRequestStatus.Approved;
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        // BR-5: reason is mandatory for an approved cancellation, optional for a pending one.
        if (wasApproved && trimmedReason is null)
            return Result<LeaveCancellationResultDto>.Failure(
                "A cancellation reason is required when cancelling an approved leave.", 400);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // AC-3 / BR-3 / FR-7: an APPROVED leave that has already started (or whose start is within
        // the tenant cancellation window) cannot be self-cancelled — HR must process it manually.
        // The window default is 0, so the block trips once start_date <= today. Pending requests
        // can be cancelled regardless of date (nothing was committed against the balance).
        if (wasApproved)
        {
            var cutoff = today.AddDays(DefaultCancellationWindowDays);
            if (request.StartDate <= cutoff)
                return Result<LeaveCancellationResultDto>.Failure(
                    "Cannot cancel leave that has already started. Please contact HR for assistance.", 400);

            // AC-4 / BR-4 (payroll): block when the leave period falls in a payroll-locked period.
            // No payroll module exists yet — this is a no-op stub that always reports "not locked".
            // TODO(payroll): when the payroll module exists, block with
            //   "Cannot cancel leave for a payroll-locked period. Please contact HR." (AC-4).
            if (await IsPayrollLockedAsync(request, cancellationToken))
                return Result<LeaveCancellationResultDto>.Failure(
                    "Cannot cancel leave for a payroll-locked period. Please contact HR.", 400);
        }

        var cancelledAt = DateTime.UtcNow;
        LeaveLedger? reversal = null;
        decimal? balanceAfter = null;

        if (wasApproved)
        {
            // AC-2 / FR-3: append a LeaveLedger "Adjusted" reversal entry with POSITIVE days to
            // restore the balance, computing balance_after from the running total (same ledger-append
            // logic as the US-LV-005 approval deduction, inverted).
            // BR-4 (carry-forward): if the original deduction consumed carry-forward days, the
            //   restoration should follow the original allocation (carry-forward days back to the
            //   carry-forward pool). The current ledger model nets Adjusted entries into the running
            //   balance, so a single positive Adjusted entry restores the FULL day count correctly.
            //   True carry-forward-pool re-allocation is NOT modelled here (it would need per-pool
            //   ledger tagging). TODO(BR-4 carry-forward-pool): when carry-forward pools are tracked
            //   distinctly, split the reversal across the original allocation buckets.
            int leaveYear = request.StartDate.Year;
            decimal currentBalance = await GetLedgerBalanceAsync(
                request.EmployeeId, request.LeaveTypeId, leaveYear, cancellationToken);

            decimal restored = currentBalance + request.TotalDays;

            reversal = new LeaveLedger
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                EntryType = LedgerEntryType.Adjusted,
                EmployeeId = request.EmployeeId,
                LeaveTypeId = request.LeaveTypeId,
                LeaveYear = leaveYear,
                LeaveRequestId = request.Id,
                Amount = request.TotalDays,           // positive — restores the balance
                BalanceAfter = restored,
                Description = $"Cancellation of leave request {request.Id}",
                OccurredAt = cancelledAt,
            };
            balanceAfter = restored;

            _dbContext.LeaveLedgerEntries.Add(reversal);
        }

        request.Status = LeaveRequestStatus.Cancelled;
        request.CancelledAt = cancelledAt;
        request.CancellationReason = trimmedReason;

        // FR-6: record the cancellation in approval history (action Cancelled, actor = self).
        var history = NewHistory(request.Id, employee.Id, LeaveApprovalAction.Cancelled, trimmedReason);
        _dbContext.LeaveApprovalHistories.Add(history);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // NFR-3 / the key race: a manager approving while the employee cancels (xmin token).
            // Mirror the US-LV-005 approve/reject conflict handling exactly.
            return Result<LeaveCancellationResultDto>.Failure(
                "This request has already been actioned.", 409);
        }

        // NFR-4: structured before/after audit (consistent with US-LV-005 — no generic audit table).
        _logger.LogInformation(
            "Leave request {LeaveRequestId} cancelled by employee {EmployeeId} (was {PriorStatus}) in tenant {TenantId}: " +
            "reversal {LedgerEntryId}, balance {BalanceAfter}. Action {AuditAction} resource {Resource}.",
            request.Id, employee.Id, wasApproved ? "Approved" : "Pending", _tenantContext.TenantId,
            reversal?.Id, balanceAfter, "Leave.Cancelled", "LeaveRequest");

        // FR-4: invalidate the Redis balance cache. Redis caching is DEFERRED module-wide (vault
        // decision); the ledger total is the single source of truth.
        // TODO(redis-balance-cache): invalidate key tenant:{tenantId}:leave_balance:{empId}:{leaveTypeId}.

        // FR-5 / AC-1/AC-2: queue a "leave-cancelled" notification to the manager (log-only seam).
        await _notificationService.NotifyLeaveCancelledAsync(
            request.Id, employee.Id, employee.ReportsToEmployeeId, trimmedReason, cancellationToken);

        return Result<LeaveCancellationResultDto>.Success(new LeaveCancellationResultDto
        {
            RequestId = request.Id,
            Status = request.Status.ToString(),
            LedgerEntryId = reversal?.Id,
            BalanceAfter = balanceAfter,
            CancelledAt = cancelledAt,
        });
    }

    /// <summary>
    /// AC-4 / BR-4 payroll-lock seam (US-LV-010). No payroll module exists yet, so this always
    /// reports "not locked". TODO(payroll): replace with a real payroll-period-lock lookup.
    /// </summary>
    private Task<bool> IsPayrollLockedAsync(LeaveRequest request, CancellationToken cancellationToken)
        => Task.FromResult(false);

    /// <summary>
    /// Shared load + authorization + state checks for approve/reject (BR-1, BR-3).
    /// Returns a populated context on success, or a context whose <c>Failure</c> is set.
    /// </summary>
    private async Task<DecisionContext> LoadForDecisionAsync(
        Guid leaveRequestId, CancellationToken cancellationToken)
    {
        if (!_tenantContext.IsResolved)
            return DecisionContext.Fail("Tenant context is not resolved.", 400);

        var manager = await GetCurrentEmployeeAsync(cancellationToken);
        if (manager is null)
            return DecisionContext.Fail("No employee record is linked to the current user.", 403);

        // Tracked load (we mutate Status). Tenant-scoped by the global query filter.
        var request = await _dbContext.LeaveRequests
            .FirstOrDefaultAsync(lr => lr.Id == leaveRequestId, cancellationToken);
        if (request is null)
            return DecisionContext.Fail("Leave request not found.", 404);

        // BR-1: only the request's approver — the manager of the requesting employee — may act.
        // Single-level direct-report scope (same as US-LV-004). Multi-level routing is deferred.
        // TODO(US-ADM-007): when a tenant approval-workflow config entity exists, route by level
        //   (1-3) and introduce the "Pending L2 Approval" status (FR-5 / AC-4, currently deferred).
        var requester = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId, cancellationToken);
        if (requester is null || requester.ReportsToEmployeeId != manager.Id)
            return DecisionContext.Fail(
                "You are not the approver for this leave request.", 403);

        // BR-3: a request that has already been actioned (Approved/Rejected/Cancelled) cannot be
        // re-actioned. Only Pending requests are actionable.
        if (request.Status != LeaveRequestStatus.Pending)
            return DecisionContext.Fail(
                $"This request has already been actioned (status: {request.Status}).", 409);

        var leaveType = await _dbContext.LeaveTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(lt => lt.Id == request.LeaveTypeId, cancellationToken);
        if (leaveType is null)
            return DecisionContext.Fail("Leave type not found.", 404);

        return new DecisionContext
        {
            Request = request,
            LeaveType = leaveType,
            Manager = manager,
        };
    }

    private LeaveApprovalHistory NewHistory(
        Guid leaveRequestId, Guid approverEmployeeId, LeaveApprovalAction action, string? comment) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantContext.TenantId,
        LeaveRequestId = leaveRequestId,
        ApproverEmployeeId = approverEmployeeId,
        ApprovalLevel = 1, // single-level only (FR-5/AC-4 multi-level deferred to US-ADM-007)
        Action = action,
        Comment = comment,
        ActionedAt = DateTime.UtcNow,
    };

    private sealed class DecisionContext
    {
        public LeaveRequest? Request { get; init; }
        public LeaveType? LeaveType { get; init; }
        public Employee? Manager { get; init; }
        public Result<LeaveApprovalResultDto>? Failure { get; init; }

        public static DecisionContext Fail(string error, int statusCode) => new()
        {
            Failure = Result<LeaveApprovalResultDto>.Failure(error, statusCode),
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
    {
        // Resolve the acting employee from the authenticated user (same pattern as
        // EmployeeDocumentService "own" checks: Employee.UserId == ICurrentUser.UserId).
        return _dbContext.Employees
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
    }

    private async Task<decimal> GetLedgerBalanceAsync(
        Guid employeeId,
        Guid leaveTypeId,
        int leaveYear,
        CancellationToken cancellationToken)
    {
        // Running balance = BalanceAfter of the most recent ledger entry (reuses US-LV-002 source).
        var lastEntry = await _dbContext.LeaveLedgerEntries
            .AsNoTracking()
            .Where(l =>
                l.EmployeeId == employeeId &&
                l.LeaveTypeId == leaveTypeId &&
                l.LeaveYear == leaveYear)
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return lastEntry?.BalanceAfter ?? 0m;
    }

    private static bool EmployeeMatchesLeaveTypeGender(Gender? employeeGender, LeaveTypeGender required)
        => required switch
        {
            LeaveTypeGender.Male => employeeGender == Gender.Male,
            LeaveTypeGender.Female => employeeGender == Gender.Female,
            _ => true,
        };

    private static LeaveRequestDto MapToDto(LeaveRequest lr, string leaveTypeName) => new()
    {
        Id = lr.Id,
        EmployeeId = lr.EmployeeId,
        LeaveTypeId = lr.LeaveTypeId,
        LeaveTypeName = leaveTypeName,
        StartDate = lr.StartDate,
        EndDate = lr.EndDate,
        IsHalfDay = lr.IsHalfDay,
        HalfDaySession = lr.HalfDaySession,
        TotalDays = lr.TotalDays,
        Reason = lr.Reason,
        Status = lr.Status.ToString(),
        RequestedAt = lr.RequestedAt,
        Attachments = lr.AttachmentUrls,
        CreatedAt = lr.CreatedAt,
        IsLop = lr.IsLop,
        LopSource = lr.LopSource?.ToString(),
    };
}
