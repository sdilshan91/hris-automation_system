using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using HRM.Application.Features.LeaveRequests;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Application.Features.LeaveTypes.DTOs;
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
    private readonly ITenantLeaveYearResolver _leaveYearResolver;
    private readonly IWorkflowRuntime? _workflowRuntime;
    private readonly ILogger<LeaveRequestService> _logger;

    // BR-1 / BR-2 configurable defaults. No tenant-settings entity exists yet, so these
    // act as constants. TODO(tenant-settings): read lookback/future window from tenant settings.
    private const int DefaultPastLookbackDays = 7;   // BR-1
    private const int DefaultFutureWindowDays = 90;  // BR-2

    // US-LV-010 FR-7 (DF-20/ISSUE-044): tenant-configurable "allow cancellation up to N days before start date".
    // The effective window is now read per-tenant from Tenant.LeaveCancellationWindowDays at cancel time; this
    // constant is only the fallback default when the tenant row cannot be resolved. Default 0 = an approved leave
    // can be cancelled any time BEFORE it starts, but is blocked once start_date <= today (BR-3, AC-3).
    private const int DefaultCancellationWindowDays = 0;

    public LeaveRequestService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IHolidayProvider holidayProvider,
        ILeaveNotificationService notificationService,
        ILogger<LeaveRequestService> logger,
        // ISSUE-305: REQUIRED, and deliberately placed before the trailing optionals. The optional pattern
        // used by its neighbours would mean a `?? date.Year` fallback — and that fallback is exactly how the
        // credit/debit ledger split hid: delete the DI registration and the build, the suite and production
        // all stay quiet while fiscal tenants silently read the wrong leave year. Better to not compile.
        ITenantLeaveYearResolver leaveYearResolver,
        IHolidayService? holidayService = null,
        ILeaveTypeService? leaveTypeService = null,
        IWorkflowRuntime? workflowRuntime = null)
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
        // US-ADM-011 FR-11: optional so the existing US-LV-003/004/005 unit tests keep compiling; DI always
        // supplies it. When present AND the tenant has an Active Leave workflow definition, submission and
        // approval route through the generic workflow runtime; otherwise the legacy single-level path runs (AC-11).
        _workflowRuntime = workflowRuntime;
        // ISSUE-305: trailing-optional for the same back-compat reason as the three above (a required
        // param reds ~72 existing fixtures). DI always supplies it; when absent LeaveYearForAsync
        // falls back to the calendar year, i.e. the exact pre-ISSUE-305 behaviour.
        _leaveYearResolver = leaveYearResolver;
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// ISSUE-305: the LeaveLedger.LeaveYear label <paramref name="date"/> falls in for this tenant.
    ///
    /// <para>This used to be a raw <c>date.Year</c> at every ledger site. That is only correct for a calendar
    /// tenant: for an Apr-Mar tenant, 2027-02-10 belongs to leave year <b>2026</b>. Because the accrual job
    /// CREDITS the ledger under the tenant's own leave year, a debit site using <c>.Year</c> would read a
    /// different bucket than the credit wrote — balance 0 → every request blocked as "insufficient balance"
    /// for the whole Jan-Mar stretch. Reads and writes must share one basis.</para>
    ///
    /// <para>Falls back to the calendar year when the resolver is absent (unit fixtures that construct this
    /// service directly); <c>LabelFor(d, 1) == d.Year</c>, so a calendar tenant is unchanged either way.</para>
    /// </summary>
    private Task<int> LeaveYearForAsync(DateOnly date, CancellationToken cancellationToken)
        => _leaveYearResolver.LabelForAsync(date, cancellationToken);

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

        // FR-3 / AC-6: Compute working days, excluding non-working week days and (when available) holidays.
        // BUG-284: the work-week is the employee's RESOLVED shift working-day set (US-ATT-011 four-tier chain),
        // never a hardcoded Mon-Fri — a Sun-Thu employee must have Sunday counted and Friday excluded.
        var workWeek = await ResolveWorkWeekAsync(employee.Id, request.StartDate, cancellationToken);

        decimal totalDays;
        if (request.IsHalfDay)
        {
            // AC-4: half-day = 0.5 day on a single working day (of THEIR week, not Mon-Fri).
            if (!workWeek.Contains(request.StartDate.DayOfWeek))
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
                request.StartDate, request.EndDate, workWeek: workWeek, holidays: holidays);
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
        int leaveYear = await LeaveYearForAsync(request.StartDate, cancellationToken);
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

        // US-ADM-011 FR-11/AC-1/AC-11: if the tenant has an Active Leave approval workflow, instantiate it now
        // so the request routes through the configured (multi-level/conditional) steps. When there is no active
        // definition the runtime returns a legacy result and WorkflowInstanceId stays null — the existing
        // single-level direct-manager approval path (US-LV-005) then governs the request unchanged (AC-11).
        if (_workflowRuntime is not null)
        {
            var requestData = new Dictionary<string, object?>
            {
                ["days_requested"] = totalDays,
                ["leave_type_id"] = effectiveLeaveTypeId,
                ["is_half_day"] = request.IsHalfDay,
                ["is_lop"] = createAsLop,
            };
            var wf = await _workflowRuntime.CreateInstanceOnSubmitAsync(
                WorkflowEntityType.Leave, leaveRequest.Id, employee.Id, requestData, cancellationToken);
            if (wf.InstanceCreated)
            {
                leaveRequest.WorkflowInstanceId = wf.InstanceId;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

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
        string? status = null,
        Guid? leaveTypeId = null,
        int? year = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<LeaveRequestDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<LeaveRequestDto>>.Failure(
                "No employee record is linked to the current user.", 403);

        var query = _dbContext.LeaveRequests
            .AsNoTracking()
            .Include(lr => lr.LeaveType)
            .Where(lr => lr.EmployeeId == employee.Id);

        // ISSUE-038 (US-LV-006 FR-6): honour the server-side history filters instead of returning the full
        // all-status list. status is parsed case-insensitively; an unparseable status narrows to nothing
        // (a quiet no-op would re-create the bug the filter is supposed to fix).
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<LeaveRequestStatus>(status, ignoreCase: true, out var statusFilter))
                return Result<IReadOnlyList<LeaveRequestDto>>.Success([]);
            query = query.Where(lr => lr.Status == statusFilter);
        }

        if (leaveTypeId.HasValue)
            query = query.Where(lr => lr.LeaveTypeId == leaveTypeId.Value);

        if (year.HasValue)
            query = query.Where(lr => lr.StartDate.Year == year.Value);

        var requests = await query
            .OrderByDescending(lr => lr.RequestedAt)
            .ToListAsync(cancellationToken);

        // DF-54: echo the tenant cancellation-notice window so the employee UI can proactively disable Cancel
        // inside it (the authoritative guard remains the 400 in CancelAsync). Read once per request, per-tenant —
        // reuses the same scoped read CancelAsync uses.
        var windowDays = await _dbContext.Tenants
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => (int?)t.LeaveCancellationWindowDays)
            .FirstOrDefaultAsync(cancellationToken) ?? DefaultCancellationWindowDays;

        var dtos = requests
            .Select(lr => MapToDto(lr, lr.LeaveType?.Name ?? string.Empty) with { CancellationWindowDays = windowDays })
            .ToList();
        return Result<IReadOnlyList<LeaveRequestDto>>.Success(dtos);
    }

    public async Task<Result<IReadOnlyList<LeaveTypeDto>>> GetEligibleLeaveTypesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<LeaveTypeDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<LeaveTypeDto>>.Failure(
                "No employee record is linked to the current user.", 403);

        // ISSUE-035: reuse the leave-type list (active-only) and apply the SAME BR-4 gender + BR-5 probation
        // gates the apply-submit path enforces, so the apply-form dropdown never lists a type the submit
        // would reject. _leaveTypeService is always supplied by DI; guard defensively for the unit fixtures.
        if (_leaveTypeService is null)
            return Result<IReadOnlyList<LeaveTypeDto>>.Failure("Leave types are not available.", 400);

        var all = await _leaveTypeService.GetAllAsync(activeOnly: true, cancellationToken);
        if (all.IsFailure)
            return Result<IReadOnlyList<LeaveTypeDto>>.Failure(all.Error!, all.StatusCode ?? 400);

        bool onProbation = employee.Status == EmployeeStatus.Probation;
        var eligible = all.Value!
            .Where(lt => IsLeaveTypeEligibleForEmployee(lt, employee.Gender, onProbation))
            .ToList();

        return Result<IReadOnlyList<LeaveTypeDto>>.Success(eligible);
    }

    /// <summary>
    /// ISSUE-035: mirrors the apply-gate eligibility (BR-4 gender + BR-5 probation) on a leave-type DTO so the
    /// eligible-types list matches what <see cref="CreateAsync"/> would accept.
    /// </summary>
    private static bool IsLeaveTypeEligibleForEmployee(LeaveTypeDto lt, Gender? employeeGender, bool onProbation)
    {
        // BR-4: gender-restricted types are only shown to matching employees.
        if (Enum.TryParse<LeaveTypeGender>(lt.Gender, ignoreCase: true, out var required)
            && required != LeaveTypeGender.All
            && !EmployeeMatchesLeaveTypeGender(employeeGender, required))
            return false;

        // BR-5: employees on probation only see probation-eligible types.
        if (onProbation && !lt.ProbationEligible)
            return false;

        return true;
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

        int leaveYear = await LeaveYearForAsync(
            startDate ?? DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
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
                // BUG-284: same resolved work-week as the deduction path above, so the preview a user sees
                // and the days actually deducted can never disagree.
                var previewWorkWeek = await ResolveWorkWeekAsync(
                    employee.Id, startDate.Value, cancellationToken);
                requestedDays = WorkingDaysCalculator.CountWorkingDays(
                    startDate.Value, endDate.Value, workWeek: previewWorkWeek, holidays: holidays);
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
                lr.EmployeeId, lr.LeaveTypeId, await LeaveYearForAsync(lr.StartDate, cancellationToken),
                cancellationToken);

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

        // ISSUE-042 (US-LV-009 FR-1 / TC-LV-182 step 4): apply a sane default range and reject an
        // excessive span rather than serving a degenerate 0001-01-01 window or an unbounded scan.
        // When BOTH bounds are omitted (they bind to DateOnly.MinValue), default to the current month.
        if (queryParams.From == default && queryParams.To == default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var monthStart = new DateOnly(today.Year, today.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            queryParams = queryParams with { From = monthStart, To = monthEnd };
        }

        if (queryParams.To < queryParams.From)
            return Result<TeamLeaveCalendarDto>.Failure("'to' must be on or after 'from'.", 400);

        // Cap the span at 366 days (a full leap year) to prevent an unbounded date-range scan.
        if (queryParams.To.DayNumber - queryParams.From.DayNumber > 366)
            return Result<TeamLeaveCalendarDto>.Failure(
                "invalid_date_range: The requested date range is too large. Please request a span of at most 366 days.",
                400);

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
            // Manager scope (BR-2, AC-2, NFR-3): requires the Leave.View.Team permission AND at
            // least one direct report. Having direct reports alone is NOT sufficient — an employee
            // holding only Leave.View.Own must never see reports' Pending requests or leave-type
            // detail (BUG-035). The Manager built-in role is seeded with Leave.View.Team.
            bool canViewTeam = _currentUser.Permissions.Contains(PermissionCatalog.Leave.ViewTeam);

            var directReportIds = canViewTeam
                ? await _dbContext.Employees
                    .AsNoTracking()
                    .Where(e => e.ReportsToEmployeeId == employee.Id)
                    .Select(e => e.Id)
                    .ToListAsync(cancellationToken)
                : new List<Guid>();

            if (canViewTeam && directReportIds.Count > 0)
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
        // US-ADM-011 FR-11: when this request is workflow-driven (WorkflowInstanceId set), route the decision
        // through the generic runtime (approver authz + multi-level advance + single-winner transaction) instead
        // of the legacy single-level direct-manager path. Returns null when the request is NOT workflow-driven,
        // in which case the legacy path below runs unchanged (AC-11).
        var routed = await TryWorkflowDecisionAsync(
            leaveRequestId, WorkflowDecisionAction.Approve, comment, cancellationToken);
        if (routed is not null)
            return routed;

        var ctx = await LoadForDecisionAsync(leaveRequestId, cancellationToken);
        if (ctx.Failure is not null)
            return ctx.Failure;

        var request = ctx.Request!;
        var leaveType = ctx.LeaveType!;
        var manager = ctx.Manager!;

        // BR-4 / AC-4 (payroll): block approval when the request period overlaps a locked payroll
        // period (the canonical AttendancePeriodLock — see IsPayrollLockedAsync). Approving a leave
        // in a finalized period would desync attendance→payroll.
        if (await IsPayrollLockedAsync(request, cancellationToken))
            return Result<LeaveApprovalResultDto>.Failure(
                "Cannot approve leave for a payroll-locked period. Please contact HR.", 400);

        // BR-5 / AC-3: recompute balance at approval time (not at request time). If insufficient
        // and the leave type does NOT allow a negative balance -> block (400). If negative is
        // allowed, the API approves (the manager "confirmation" modal is a UI concern, AC-3).
        int leaveYear = await LeaveYearForAsync(request.StartDate, cancellationToken);
        decimal currentBalance = await GetLedgerBalanceAsync(
            request.EmployeeId, leaveType.Id, leaveYear, cancellationToken);

        decimal projected = currentBalance - request.TotalDays;
        if (projected < 0m && !leaveType.NegativeBalanceAllowed)
        {
            return Result<LeaveApprovalResultDto>.Failure(
                $"Insufficient leave balance to approve. Available: {currentBalance} day(s), requested: {request.TotalDays} day(s).",
                400);
        }

        // BUG-029 (US-LV-006): even when the leave type permits a negative balance, enforce a hard
        // floor at approval time. The balance may go negative only down to -NegativeBalanceLimit; an
        // approval that would push it past the configured floor is rejected (400). A null limit on a
        // negative-allowed type means "no floor" (unbounded negative), so the check is skipped.
        if (projected < 0m && leaveType.NegativeBalanceAllowed && leaveType.NegativeBalanceLimit is decimal negativeLimit
            && projected < -negativeLimit)
        {
            return Result<LeaveApprovalResultDto>.Failure(
                $"negative_balance_limit_exceeded: Approving would exceed the allowed negative balance. " +
                $"Available: {currentBalance} day(s), requested: {request.TotalDays} day(s), " +
                $"negative-balance limit: {negativeLimit} day(s).",
                400);
        }

        // FR-3 / AC-1: append the LeaveLedger "Used" deduction, linked to the request, computing
        // balance_after from the running total (reuses the US-LV-002 accrual ledger-append logic).
        // DF-19 / ISSUE-045: PERSIST the BR-4 FIFO pool split — carry-forward days are consumed
        // before accrual days, each row tagged with its pool + bucket, so a later cancellation
        // restores each pool exactly and the expiry job reads the persisted ConsumedDays counter.
        // The rows net to −TotalDays and the final row's BalanceAfter == projected (unchanged aggregate).
        var ledgerEntry = await AppendPooledDeductionAsync(
            request, leaveType, leaveYear, currentBalance,
            $"Leave approved ({leaveType.Name}): {request.TotalDays} day(s)",
            DateTime.UtcNow, cancellationToken);

        var priorStatus = request.Status;
        request.Status = LeaveRequestStatus.Approved;

        var history = NewHistory(request.Id, manager.Id, LeaveApprovalAction.Approved, comment);

        _dbContext.LeaveApprovalHistories.Add(history);

        // ISSUE-037 / FR-7: emit a DISTINCT semantic audit action (Leave.Approved) with a before/after
        // status snapshot + decision context, so the trail is queryable by action (the generic
        // AuditCaptureInterceptor separately records the LeaveRequest.Update field diff). Added to the same
        // change set so it commits atomically with the status change + ledger deduction.
        AddDecisionAudit("Leave.Approved", request, manager.Id, priorStatus, request.Status, comment);

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

        // US-ADM-011 FR-11: route a workflow-driven request's rejection through the runtime (see ApproveAsync).
        var routed = await TryWorkflowDecisionAsync(
            leaveRequestId, WorkflowDecisionAction.Reject, reason, cancellationToken);
        if (routed is not null)
            return routed;

        var ctx = await LoadForDecisionAsync(leaveRequestId, cancellationToken);
        if (ctx.Failure is not null)
            return ctx.Failure;

        var request = ctx.Request!;
        var manager = ctx.Manager!;

        // FR-4: no ledger entry on rejection — only a status update and approval-history record.
        var priorStatus = request.Status;
        request.Status = LeaveRequestStatus.Rejected;

        var history = NewHistory(request.Id, manager.Id, LeaveApprovalAction.Rejected, reason);
        _dbContext.LeaveApprovalHistories.Add(history);

        // ISSUE-037 / FR-7: emit a DISTINCT semantic audit action (Leave.Rejected) capturing the status
        // transition + the rejection reason (see ApproveAsync).
        AddDecisionAudit("Leave.Rejected", request, manager.Id, priorStatus, request.Status, reason);

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
    //  Workflow-driven approval routing (US-ADM-011 FR-11)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// US-ADM-011 FR-11: routes an approve/reject decision through the generic workflow runtime when the leave
    /// request is workflow-driven (its <see cref="LeaveRequest.WorkflowInstanceId"/> is set). Returns
    /// <c>null</c> when the request is NOT workflow-driven (or the runtime is unavailable), so the caller runs
    /// the legacy single-level path unchanged (AC-11). The leave domain side-effects (balance floor + payroll
    /// lock + ledger deduction / status change) run inside the runtime's transaction via the callbacks, so the
    /// workflow state and the leave state commit atomically (AC-12).
    /// </summary>
    private async Task<Result<LeaveApprovalResultDto>?> TryWorkflowDecisionAsync(
        Guid leaveRequestId, WorkflowDecisionAction action, string? comment, CancellationToken cancellationToken)
    {
        if (_workflowRuntime is null || !_tenantContext.IsResolved)
            return null;

        var peek = await _dbContext.LeaveRequests.AsNoTracking()
            .FirstOrDefaultAsync(lr => lr.Id == leaveRequestId, cancellationToken);
        if (peek is null || peek.WorkflowInstanceId is null)
            return null; // legacy single-level path (AC-11)

        Guid? ledgerEntryId = null;
        decimal? balanceAfter = null;

        var decision = await _workflowRuntime.DecideAsync(
            WorkflowEntityType.Leave, leaveRequestId, action, comment,
            onApproved: async ct =>
            {
                var staged = await StageLeaveApprovalAsync(leaveRequestId, comment, ct);
                if (staged.IsFailure)
                    return Result.Failure(staged.Error!, staged.StatusCode ?? 400, staged.ErrorCode);
                ledgerEntryId = staged.Value!.LedgerEntryId;
                balanceAfter = staged.Value!.BalanceAfter;
                return Result.Success();
            },
            onRejected: ct => StageLeaveRejectionAsync(leaveRequestId, comment, ct),
            cancellationToken: cancellationToken);

        if (decision.IsFailure)
            return Result<LeaveApprovalResultDto>.Failure(
                decision.Error!, decision.StatusCode ?? 400, decision.ErrorCode);

        var outcome = decision.Value!;
        var now = DateTime.UtcNow;

        switch (outcome.Outcome)
        {
            case WorkflowDecisionOutcome.StepAdvanced:
            case WorkflowDecisionOutcome.StepRecorded:
                // Intermediate approval — the request stays Pending. StepAdvanced = the group completed and the
                // instance moved to the next step; StepRecorded (US-ADM-011b, AC-4) = a parallel co-approver
                // approved but the group still awaits other approvers. Either way the leave request stays Pending.
                _logger.LogInformation(
                    "Leave request {LeaveRequestId} approval recorded at workflow step {StepOrder} ({Outcome}) in tenant {TenantId}.",
                    leaveRequestId, outcome.NextStepOrder, outcome.Outcome, _tenantContext.TenantId);
                return Result<LeaveApprovalResultDto>.Success(new LeaveApprovalResultDto
                {
                    RequestId = leaveRequestId,
                    Status = LeaveRequestStatus.Pending.ToString(),
                    Action = LeaveApprovalAction.Approved.ToString(),
                    ApprovalLevel = outcome.NextStepOrder ?? 0,
                    ActionedAt = now,
                });

            case WorkflowDecisionOutcome.InstanceApproved:
                // BUG-309: this parameter is approverEmployeeId -- an EMPLOYEE id. It used to be passed
                // _currentUser.UserId, a USER id, so every workflow-driven approval notification recorded the
                // wrong identity type. The legacy path passes manager.Id correctly, and StageLeaveApprovalAsync
                // forty lines below already resolves an employee id for the LeaveApprovalHistory row -- the
                // method was inconsistent with itself, which is what makes this a defect and not a convention.
                await _notificationService.NotifyLeaveApprovedAsync(
                    leaveRequestId, peek.EmployeeId,
                    await ResolveActingEmployeeIdAsync(cancellationToken), cancellationToken);
                return Result<LeaveApprovalResultDto>.Success(new LeaveApprovalResultDto
                {
                    RequestId = leaveRequestId,
                    Status = LeaveRequestStatus.Approved.ToString(),
                    Action = LeaveApprovalAction.Approved.ToString(),
                    ApprovalLevel = outcome.NextStepOrder ?? 0,
                    LedgerEntryId = ledgerEntryId,
                    BalanceAfter = balanceAfter,
                    ActionedAt = now,
                });

            case WorkflowDecisionOutcome.InstanceRejected:
            default:
                // BUG-309: same wrong-identity-type defect as the approve branch above.
                await _notificationService.NotifyLeaveRejectedAsync(
                    leaveRequestId, peek.EmployeeId,
                    await ResolveActingEmployeeIdAsync(cancellationToken),
                    comment ?? string.Empty, cancellationToken);
                return Result<LeaveApprovalResultDto>.Success(new LeaveApprovalResultDto
                {
                    RequestId = leaveRequestId,
                    Status = LeaveRequestStatus.Rejected.ToString(),
                    Action = LeaveApprovalAction.Rejected.ToString(),
                    ApprovalLevel = 0,
                    ActionedAt = now,
                });
        }
    }

    /// <summary>
    /// Stages the leave-approval side-effects (US-LV-005 balance floor + payroll-lock guard + ledger "Used"
    /// deduction + status → Approved + approval-history) WITHOUT saving — the workflow runtime owns the
    /// SaveChanges/commit so the workflow completion and the ledger deduction are one atomic unit. Mirrors the
    /// legacy <see cref="ApproveAsync"/> ledger logic for the workflow-driven path.
    /// </summary>
    private async Task<Result<(Guid LedgerEntryId, decimal BalanceAfter)>> StageLeaveApprovalAsync(
        Guid leaveRequestId, string? comment, CancellationToken cancellationToken)
    {
        var request = await _dbContext.LeaveRequests
            .FirstOrDefaultAsync(lr => lr.Id == leaveRequestId, cancellationToken);
        if (request is null)
            return Result<(Guid, decimal)>.Failure("Leave request not found.", 404);

        var leaveType = await _dbContext.LeaveTypes.AsNoTracking()
            .FirstOrDefaultAsync(lt => lt.Id == request.LeaveTypeId, cancellationToken);
        if (leaveType is null)
            return Result<(Guid, decimal)>.Failure("Leave type not found.", 404);

        // BR-4 / AC-4: block completion in a locked payroll period (same guard as the legacy path).
        if (await IsPayrollLockedAsync(request, cancellationToken))
            return Result<(Guid, decimal)>.Failure(
                "Cannot approve leave for a payroll-locked period. Please contact HR.", 400);

        int leaveYear = await LeaveYearForAsync(request.StartDate, cancellationToken);
        decimal currentBalance = await GetLedgerBalanceAsync(
            request.EmployeeId, leaveType.Id, leaveYear, cancellationToken);
        decimal projected = currentBalance - request.TotalDays;

        // BR-5 / AC-3 + BUG-029 floor: honor the same balance guards as the legacy approval.
        if (projected < 0m && !leaveType.NegativeBalanceAllowed)
            return Result<(Guid, decimal)>.Failure(
                $"Insufficient leave balance to approve. Available: {currentBalance} day(s), requested: {request.TotalDays} day(s).", 400);
        if (projected < 0m && leaveType.NegativeBalanceAllowed && leaveType.NegativeBalanceLimit is decimal negativeLimit
            && projected < -negativeLimit)
            return Result<(Guid, decimal)>.Failure(
                $"negative_balance_limit_exceeded: Approving would exceed the allowed negative balance. " +
                $"Available: {currentBalance} day(s), requested: {request.TotalDays} day(s), negative-balance limit: {negativeLimit} day(s).", 400);

        // DF-19 / ISSUE-045: same PERSISTED BR-4 FIFO pool split as the legacy ApproveAsync path —
        // carry-forward days consumed before accrual days, tagged per pool + bucket, netting −TotalDays
        // with the final row's BalanceAfter == projected. Staged (not saved) — the workflow runtime commits.
        var ledgerEntry = await AppendPooledDeductionAsync(
            request, leaveType, leaveYear, currentBalance,
            $"Leave approved via workflow ({leaveType.Name}): {request.TotalDays} day(s)",
            DateTime.UtcNow, cancellationToken);

        // ISSUE-387: capture BEFORE the flip -- the audit row records the transition, not the end state.
        var priorStatus = request.Status;
        request.Status = LeaveRequestStatus.Approved;

        var approverEmployeeId = await ResolveActingEmployeeIdAsync(cancellationToken);
        _dbContext.LeaveApprovalHistories.Add(NewHistory(
            request.Id, approverEmployeeId, LeaveApprovalAction.Approved, comment));

        // ISSUE-387: the SEMANTIC audit row, identical in shape to the legacy path's.
        //
        // The engine already writes workflow.instance.approved with ResourceType "WorkflowInstance". That
        // records that a workflow STEP was approved; ISSUE-037/FR-7 is about a LEAVE REQUEST being approved,
        // and requires the trail to be queryable BY ACTION. Before C1 every tenant used the legacy path, so
        // the by-action trail was complete; C1 made the engine the live path for everyone, which would have
        // silently dropped every workflow-driven decision out of that query. Staged (not saved) -- the
        // workflow runtime owns the commit, so the audit row lands in the same transaction as the decision.
        AddDecisionAudit(
            "Leave.Approved", request, approverEmployeeId, priorStatus, request.Status, comment);

        return Result<(Guid, decimal)>.Success((ledgerEntry.Id, projected));
    }

    /// <summary>
    /// Stages the leave-rejection side-effects (status → Rejected + approval-history) WITHOUT saving — the
    /// workflow runtime owns the commit. Mirrors the legacy <see cref="RejectAsync"/> for the workflow path.
    /// </summary>
    private async Task StageLeaveRejectionAsync(Guid leaveRequestId, string? reason, CancellationToken cancellationToken)
    {
        var request = await _dbContext.LeaveRequests
            .FirstOrDefaultAsync(lr => lr.Id == leaveRequestId, cancellationToken);
        if (request is null)
            return;

        // ISSUE-387: capture BEFORE the flip.
        var priorStatus = request.Status;
        request.Status = LeaveRequestStatus.Rejected;
        var approverEmployeeId = await ResolveActingEmployeeIdAsync(cancellationToken);
        _dbContext.LeaveApprovalHistories.Add(NewHistory(
            request.Id, approverEmployeeId, LeaveApprovalAction.Rejected, reason));

        // ISSUE-387: the semantic audit row -- see StageLeaveApprovalAsync for why the engine's own
        // workflow.instance.rejected row is not a substitute.
        AddDecisionAudit(
            "Leave.Rejected", request, approverEmployeeId, priorStatus, request.Status, reason);
    }

    /// <summary>Resolves the acting user's employee id for approval-history (Guid.Empty if none is linked).</summary>
    private async Task<Guid> ResolveActingEmployeeIdAsync(CancellationToken cancellationToken)
    {
        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        return employee?.Id ?? Guid.Empty;
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
            // DF-20/ISSUE-044: the notice window is tenant-configurable (Tenant.LeaveCancellationWindowDays).
            // Scoped read of the current tenant's value; falls back to the const default if the row is missing.
            var windowDays = await _dbContext.Tenants
                .Where(t => t.Id == _tenantContext.TenantId)
                .Select(t => (int?)t.LeaveCancellationWindowDays)
                .FirstOrDefaultAsync(cancellationToken) ?? DefaultCancellationWindowDays;
            var cutoff = today.AddDays(windowDays);
            if (request.StartDate <= cutoff)
                return Result<LeaveCancellationResultDto>.Failure(
                    "Cannot cancel leave that has already started. Please contact HR for assistance.", 400);

            // AC-4 / BR-4 (payroll): block when the leave period overlaps a locked payroll period
            // (the canonical AttendancePeriodLock — see IsPayrollLockedAsync).
            if (await IsPayrollLockedAsync(request, cancellationToken))
                return Result<LeaveCancellationResultDto>.Failure(
                    "Cannot cancel leave for a payroll-locked period. Please contact HR.", 400);
        }

        var cancelledAt = DateTime.UtcNow;
        LeaveLedger? reversal = null;
        decimal? balanceAfter = null;

        if (wasApproved)
        {
            // AC-2 / FR-3: append LeaveLedger "Adjusted" reversal rows with POSITIVE days to restore
            // the balance, chaining balance_after from the running total (the approval deduction,
            // inverted).
            // DF-19 / ISSUE-045 (BR-4 carry-forward): restore PER POOL. Mirror THIS request's persisted
            // Used deduction rows (the FIFO pool split written at approval): carry-forward days go back
            // to their bucket (decrementing ConsumedDays, preserving the ORIGINAL ExpiryDate — never
            // refreshed) and accrual days back to general balance. The rows sum to +request.TotalDays,
            // so the aggregate running balance is byte-identical to the pre-DF-19 single reversal.
            int leaveYear = await LeaveYearForAsync(request.StartDate, cancellationToken);
            decimal currentBalance = await GetLedgerBalanceAsync(
                request.EmployeeId, request.LeaveTypeId, leaveYear, cancellationToken);

            // This request's own Used deduction rows, oldest-first so the running balance chains
            // deterministically. Tenant-scoped by the global query filter.
            var usedRows = await _dbContext.LeaveLedgerEntries
                .AsNoTracking()
                .Where(l => l.LeaveRequestId == request.Id && l.EntryType == LedgerEntryType.Used)
                .OrderBy(l => l.OccurredAt).ThenBy(l => l.CreatedAt)
                .ToListAsync(cancellationToken);

            decimal running = currentBalance;
            int idx = 0;

            if (usedRows.Count == 0)
            {
                // Legacy / non-pool-tagged deduction (approved before DF-19, or a request whose Used
                // row was not request-linked): restore the full day count as one untagged Adjusted
                // entry — byte-identical to the pre-DF-19 behavior.
                running += request.TotalDays;
                reversal = BuildRestoreRow(request, leaveYear, pool: null, trackingId: null,
                    amount: request.TotalDays, balanceAfter: running,
                    description: $"Cancellation of leave request {request.Id}",
                    occurredAt: cancelledAt);
                _dbContext.LeaveLedgerEntries.Add(reversal);
            }
            else
            {
                bool multi = usedRows.Count > 1;
                foreach (var used in usedRows)
                {
                    decimal restoreAmount = -used.Amount; // used.Amount is negative -> positive restore
                    // Each restore row is stamped +idx µs so the final row is unambiguously latest
                    // (GetLedgerBalanceAsync reads the latest OccurredAt's BalanceAfter). idx 0 == no
                    // offset -> a single restore row keeps cancelledAt byte-identical.
                    var occurredAt = cancelledAt.AddTicks(PooledLeaveLedger.PoolRowTickOffset * idx);

                    if (used.Pool == LeavePool.CarryForward && used.CarryForwardTrackingId is Guid trackingId)
                    {
                        var tracking = await _dbContext.LeaveCarryForwardTrackings
                            .FirstOrDefaultAsync(t => t.Id == trackingId, cancellationToken);

                        if (tracking is not null && tracking.Status != CarryForwardTrackingStatus.Expired)
                        {
                            // Un-consume: return the carried days to their bucket. ExpiryDate is
                            // UNTOUCHED (preserve — restored carry-forward days keep their ORIGINAL
                            // expiry). If the bucket had been fully Consumed and now has remaining
                            // days again, re-open it so the expiry sweep can still forfeit any residue.
                            tracking.ConsumedDays = Math.Max(0m, tracking.ConsumedDays - restoreAmount);
                            if (tracking.Status == CarryForwardTrackingStatus.Consumed
                                && tracking.CarriedDays - tracking.ConsumedDays - tracking.ExpiredDays > 0m)
                                tracking.Status = CarryForwardTrackingStatus.Active;

                            running += restoreAmount;
                            reversal = BuildRestoreRow(request, leaveYear, LeavePool.CarryForward, trackingId,
                                restoreAmount, running,
                                $"Cancellation of leave request {request.Id} [carry-forward pool: {restoreAmount} day(s)]",
                                occurredAt);
                            _dbContext.LeaveLedgerEntries.Add(reversal);
                        }
                        else
                        {
                            // The bucket already TERMINALLY expired — its carried days were forfeited
                            // and cannot be un-expired. Restore the days as general (Accrual-pool)
                            // balance instead of resurrecting a dead expired bucket (DF-19 fallback).
                            _logger.LogWarning(
                                "Leave cancel {LeaveRequestId}: carry-forward bucket {TrackingId} is already Expired; " +
                                "restoring {Days} day(s) to accrual (general) balance instead of the forfeited bucket. Tenant {TenantId}.",
                                request.Id, trackingId, restoreAmount, _tenantContext.TenantId);

                            running += restoreAmount;
                            reversal = BuildRestoreRow(request, leaveYear, LeavePool.Accrual, trackingId: null,
                                restoreAmount, running,
                                $"Cancellation of leave request {request.Id} [accrual pool (expired carry-forward forfeited): {restoreAmount} day(s)]",
                                occurredAt);
                            _dbContext.LeaveLedgerEntries.Add(reversal);
                        }
                    }
                    else
                    {
                        // Accrual pool (or an untagged Used row): restore to general balance.
                        running += restoreAmount;
                        reversal = BuildRestoreRow(request, leaveYear, used.Pool, trackingId: null,
                            restoreAmount, running,
                            multi
                                ? $"Cancellation of leave request {request.Id} [accrual pool: {restoreAmount} day(s)]"
                                : $"Cancellation of leave request {request.Id}",
                            occurredAt);
                        _dbContext.LeaveLedgerEntries.Add(reversal);
                    }

                    idx++;
                }
            }

            balanceAfter = running; // == currentBalance + request.TotalDays (aggregate preserved)
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
    /// AC-4 / BR-4 payroll-lock check (US-LV-010 / US-LV-005 BR-4). The leave falls in a locked payroll
    /// period when its date range [<see cref="LeaveRequest.StartDate"/>, <see cref="LeaveRequest.EndDate"/>]
    /// overlaps any ACTIVE payroll period lock for the tenant. Reuses the canonical
    /// <c>AttendancePeriodLock</c> — the same mechanism <c>AttendancePayrollService.GetPeriodLockAsync</c>
    /// reports (US-ATT-009) — rather than inventing a new lock store. Tenant isolation via the EF global
    /// query filter.
    /// </summary>
    private Task<bool> IsPayrollLockedAsync(LeaveRequest request, CancellationToken cancellationToken)
        => _dbContext.AttendancePeriodLocks.AsNoTracking()
            .AnyAsync(p => p.IsLocked
                           && p.PeriodStart <= request.EndDate
                           && p.PeriodEnd >= request.StartDate,
                cancellationToken);

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

    /// <summary>
    /// ISSUE-037 / FR-7: adds a DISTINCT semantic audit row (Leave.Approved / Leave.Rejected) to the change
    /// set WITHOUT saving — the caller's SaveChanges commits it atomically with the decision. Captures the
    /// labeled status transition (before/after) plus the deciding manager and the comment/reason, so an
    /// auditor can filter the trail by action rather than only seeing the generic LeaveRequest.Update.
    /// </summary>
    private void AddDecisionAudit(
        string action, LeaveRequest request, Guid approverEmployeeId,
        LeaveRequestStatus before, LeaveRequestStatus after, string? comment)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            UserId = _currentUser.IsAuthenticated ? _currentUser.UserId : null,
            EventType = action,
            Action = action,
            ResourceType = "LeaveRequest",
            ResourceId = request.Id.ToString(),
            Before = JsonSerializer.Serialize(new { Status = before.ToString() }),
            After = JsonSerializer.Serialize(new
            {
                Status = after.ToString(),
                ApproverEmployeeId = approverEmployeeId,
                Comment = comment,
            }),
            CreatedAt = DateTime.UtcNow,
        });
    }

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

    /// <summary>
    /// DF-19 / ISSUE-045: appends the leave-approval "Used" deduction, PERSISTING the BR-4 FIFO pool
    /// split. Consumes the active carry-forward bucket for this leave year BEFORE accrual days,
    /// writing up to two Used rows — each tagged with its <see cref="LeavePool"/> (and the carry row
    /// with its bucket id) — and chaining BalanceAfter from <paramref name="currentBalance"/>. The
    /// rows net to −<c>request.TotalDays</c> and the final row's BalanceAfter equals
    /// <c>currentBalance − TotalDays</c> — byte-identical in aggregate to the pre-DF-19 single row.
    /// Increments the bucket's <c>ConsumedDays</c> so cancel (restore) and the expiry job agree.
    /// Adds rows to the change set but does NOT save; returns the FINAL row (its Id + BalanceAfter
    /// back the approval result).
    /// </summary>
    private async Task<LeaveLedger> AppendPooledDeductionAsync(
        LeaveRequest request,
        LeaveType leaveType,
        int leaveYear,
        decimal currentBalance,
        string description,
        DateTime occurredAt,
        CancellationToken cancellationToken)
        => (await PooledLeaveLedger.AppendDeductionAsync(
            _dbContext, _tenantContext.TenantId, request.EmployeeId, leaveType.Id, leaveYear,
            request.TotalDays, request.Id, currentBalance, description, occurredAt,
            LedgerEntryType.Used, cancellationToken)).FinalRow;

    /// <summary>
    /// DF-19 / ISSUE-045: builds a single positive "Adjusted" cancel-restore ledger row, tagged with
    /// the pool (+ bucket) it restores. Does NOT add it to the change set — the caller does, so it can
    /// chain BalanceAfter deterministically across a multi-pool restore.
    /// </summary>
    private LeaveLedger BuildRestoreRow(
        LeaveRequest request, int leaveYear, LeavePool? pool, Guid? trackingId,
        decimal amount, decimal balanceAfter, string description, DateTime occurredAt) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = _tenantContext.TenantId,
        EntryType = LedgerEntryType.Adjusted,
        EmployeeId = request.EmployeeId,
        LeaveTypeId = request.LeaveTypeId,
        LeaveYear = leaveYear,
        LeaveRequestId = request.Id,
        Pool = pool,
        CarryForwardTrackingId = trackingId,
        Amount = amount,           // positive — restores the balance
        BalanceAfter = balanceAfter,
        Description = description,
        OccurredAt = occurredAt,
    };

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

    /// <summary>
    /// BUG-284 / US-ATT-011 AC-2: resolves the employee's working week as of <paramref name="asOf"/> via the
    /// shared four-tier chain (Employee shift → Location default → tenant default → Mon–Fri code default), so
    /// leave day-counting uses the SAME working-week source as attendance and payroll instead of a hardcoded
    /// Mon–Fri. A Gulf (Sun–Thu) employee therefore has Sunday counted as a leave day and Friday excluded.
    ///
    /// <para><b>As-of semantics:</b> the work-week is resolved once, at the leave's START date — the resolver
    /// is as-of a single date, and this matches the payroll convention. A shift change taking effect part-way
    /// through a leave range is therefore not split across the range; that is a deliberate simplification, not
    /// an oversight.</para>
    /// </summary>
    private async Task<IReadOnlySet<DayOfWeek>> ResolveWorkWeekAsync(
        Guid employeeId, DateOnly asOf, CancellationToken cancellationToken)
    {
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            _dbContext, [employeeId], asOf, cancellationToken);

        // The resolver never returns an empty set (its tier-4 code default + ToCalendar guarantee that), but
        // fall back explicitly rather than trusting it: an empty set here would silently mean "no day is a
        // working day" to WorkingDaysCalculator's Contains checks — the opposite of the resolver's own
        // empty-set sentinel. Never let the two conventions meet.
        return sets.TryGetValue(employeeId, out var isoDays) && isoDays.Count > 0
            ? ToDayOfWeekSet(isoDays)
            : WorkingDaysCalculator.DefaultWorkWeek;
    }

    /// <summary>
    /// Converts the resolver's ISO working-day set (1=Mon..7=Sun) to the <see cref="DayOfWeek"/> set
    /// <see cref="WorkingDaysCalculator"/> expects (Sun=0..Sat=6). The two halves of the leave path use
    /// different day conventions, and <c>(DayOfWeek)(iso % 7)</c> is the whole bridge: ISO 7 (Sunday) → 0
    /// (<see cref="DayOfWeek.Sunday"/>), ISO 1..6 → 1..6 (Mon..Sat) unchanged.
    /// </summary>
    private static IReadOnlySet<DayOfWeek> ToDayOfWeekSet(HashSet<int> isoDays) =>
        isoDays.Select(iso => (DayOfWeek)(iso % 7)).ToHashSet();

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
