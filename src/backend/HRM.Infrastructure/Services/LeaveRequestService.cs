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
    private readonly ILeaveNotificationService _notificationService;
    private readonly ILogger<LeaveRequestService> _logger;

    // BR-1 / BR-2 configurable defaults. No tenant-settings entity exists yet, so these
    // act as constants. TODO(tenant-settings): read lookback/future window from tenant settings.
    private const int DefaultPastLookbackDays = 7;   // BR-1
    private const int DefaultFutureWindowDays = 90;  // BR-2

    public LeaveRequestService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IHolidayProvider holidayProvider,
        ILeaveNotificationService notificationService,
        ILogger<LeaveRequestService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _holidayProvider = holidayProvider;
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
            var holidays = await _holidayProvider.GetHolidaysAsync(
                request.StartDate, request.EndDate, cancellationToken);
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

        decimal projected = currentBalance - totalDays;
        if (projected < 0m)
        {
            if (!leaveType.NegativeBalanceAllowed)
                return Result<LeaveRequestDto>.Failure(
                    $"Insufficient leave balance. Available: {currentBalance} day(s), requested: {totalDays} day(s).", 400);

            if (leaveType.NegativeBalanceLimit.HasValue && -projected > leaveType.NegativeBalanceLimit.Value)
                return Result<LeaveRequestDto>.Failure(
                    $"Request exceeds the allowed negative balance limit of {leaveType.NegativeBalanceLimit.Value} day(s).", 400);
        }

        var leaveRequest = new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employee.Id,
            LeaveTypeId = leaveType.Id,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsHalfDay = request.IsHalfDay,
            HalfDaySession = request.IsHalfDay ? request.HalfDaySession?.ToUpperInvariant() : null,
            TotalDays = totalDays,
            Reason = request.Reason,
            Status = LeaveRequestStatus.Pending,
            RequestedAt = DateTime.UtcNow,
            AttachmentUrls = attachments,
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

        return Result<LeaveRequestDto>.Success(MapToDto(leaveRequest, leaveType.Name));
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
                var holidays = await _holidayProvider.GetHolidaysAsync(
                    startDate.Value, endDate.Value, cancellationToken);
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
    };
}
