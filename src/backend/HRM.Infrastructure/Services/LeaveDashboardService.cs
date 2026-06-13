using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Employee Leave Balance Dashboard read/aggregation service (US-LV-006).
/// Pure read over existing data — no new entity or migration. All queries are tenant-scoped via
/// ITenantContext + EF global query filters and resolve the acting employee from the current user
/// (self-service: an employee can only ever read their own data).
///
/// Balances are computed component-wise from the LeaveLedger (BR-1):
///   Balance = Entitlement + CarryForward - Used - Expired + Adjustments
/// The entitlement value reuses the US-LV-002 entitlement engine (override &gt; rule &gt; default,
/// pro-rated) via ILeaveEntitlementService.ComputeEffectiveEntitlementAsync.
///
/// Leave-year boundary (BR-4): the codebase has no tenant fiscal-year config entity, so the
/// established calendar-year convention from US-LV-002..005 is reused (the year a request/ledger
/// entry falls in). A `year` parameter lets the employee view previous years (BR-5).
///
/// Redis balance caching (FR-5/NFR-1) is DEFERRED, consistent with the module-wide decision.
/// TODO(redis-balance-cache): wrap GetMyBalancesAsync in an IDistributedCache layer keyed
///   tenant:{tenantId}:leave_balance:{employeeId}:{leaveTypeId} with invalidation on ledger writes.
/// </summary>
public sealed class LeaveDashboardService : ILeaveDashboardService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILeaveEntitlementService _entitlementService;
    private readonly ILogger<LeaveDashboardService> _logger;

    public LeaveDashboardService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILeaveEntitlementService entitlementService,
        ILogger<LeaveDashboardService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _entitlementService = entitlementService;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-1/FR-2, AC-1, AC-5: balances per leave type
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<LeaveBalanceDto>>> GetMyBalancesAsync(
        int? year,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<LeaveBalanceDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<LeaveBalanceDto>>.Failure(
                "No employee record is linked to the current user.", 403);

        int leaveYear = ResolveLeaveYear(year);

        // The leave types the employee has ANY ledger activity for in this year (drives archived
        // detection for deactivated types, BR-3). Tenant-scoped by the global query filter.
        var ledgerTypeIds = await _dbContext.LeaveLedgerEntries
            .AsNoTracking()
            .Where(l => l.EmployeeId == employee.Id && l.LeaveYear == leaveYear)
            .Select(l => l.LeaveTypeId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // BR-3: active types are always shown; deactivated types are only shown if they still
        // carry ledger activity for the year (the non-zero-balance check is applied below).
        var leaveTypes = await _dbContext.LeaveTypes
            .AsNoTracking()
            .Where(lt => lt.IsActive || ledgerTypeIds.Contains(lt.Id))
            .OrderBy(lt => lt.DisplayOrder)
            .ThenBy(lt => lt.Name)
            .ToListAsync(cancellationToken);

        // AC-5: a brand-new joiner with no leave types configured / no data -> empty list, no throw.
        if (leaveTypes.Count == 0)
            return Result<IReadOnlyList<LeaveBalanceDto>>.Success(Array.Empty<LeaveBalanceDto>());

        var leaveTypeIds = leaveTypes.Select(lt => lt.Id).ToList();

        // One pass over the employee's ledger for the year: component sums per leave type (BR-1).
        var ledgerEntries = await _dbContext.LeaveLedgerEntries
            .AsNoTracking()
            .Where(l => l.EmployeeId == employee.Id
                        && l.LeaveYear == leaveYear
                        && leaveTypeIds.Contains(l.LeaveTypeId))
            .Select(l => new { l.LeaveTypeId, l.EntryType, l.Amount })
            .ToListAsync(cancellationToken);

        var components = ledgerEntries
            .GroupBy(l => l.LeaveTypeId)
            .ToDictionary(
                g => g.Key,
                g => LedgerComponents.From(g.Select(e => (e.EntryType, e.Amount))));

        // BR-2: pending days per leave type from the employee's currently Pending requests.
        // Pending is shown separately and NOT subtracted from balance.
        var pendingByType = (await _dbContext.LeaveRequests
                .AsNoTracking()
                .Where(lr => lr.EmployeeId == employee.Id
                             && lr.Status == LeaveRequestStatus.Pending
                             && lr.StartDate.Year == leaveYear
                             && leaveTypeIds.Contains(lr.LeaveTypeId))
                .Select(lr => new { lr.LeaveTypeId, lr.TotalDays })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.LeaveTypeId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.TotalDays));

        var result = new List<LeaveBalanceDto>(leaveTypes.Count);

        foreach (var lt in leaveTypes)
        {
            var c = components.TryGetValue(lt.Id, out var comp) ? comp : LedgerComponents.Empty;

            // Entitlement reuses the US-LV-002 engine (override > rule > default, pro-rated).
            decimal entitlement = await ResolveEntitlementAsync(employee.Id, lt.Id, leaveYear, cancellationToken);

            // BR-1: Balance = Entitlement + CarryForward - Used - Expired + Adjustments.
            // The entitlement engine's value is the granted allowance; the ledger Accrual entries
            // realise it. We surface the engine entitlement (the story's "resolved entitlement") and
            // derive the balance from the ledger components so the displayed math is self-consistent
            // with the running ledger the rest of the module uses.
            decimal balance = entitlement + c.CarryForward - c.Used - c.Expired + c.Adjustments;

            decimal pending = pendingByType.TryGetValue(lt.Id, out var p) ? p : 0m;

            bool isArchived = !lt.IsActive;

            // BR-3: a deactivated type is only surfaced (in the Archived section) when it still
            // carries a non-zero balance for the year; otherwise it is dropped entirely.
            if (isArchived && balance == 0m)
                continue;

            result.Add(new LeaveBalanceDto
            {
                LeaveTypeId = lt.Id,
                LeaveTypeName = lt.Name,
                Color = lt.Color,
                Entitlement = entitlement,
                Used = c.Used,
                Pending = pending,
                CarryForward = c.CarryForward,
                Expired = c.Expired,
                Adjustments = c.Adjustments,
                Balance = balance,
                LeaveYear = leaveYear,
                IsArchived = isArchived,
            });
        }

        return Result<IReadOnlyList<LeaveBalanceDto>>.Success(result);
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-3, AC-2: ledger transaction log
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<LeaveLedgerEntryDto>>> GetMyLedgerAsync(
        Guid leaveTypeId,
        int? year,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<LeaveLedgerEntryDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<LeaveLedgerEntryDto>>.Failure(
                "No employee record is linked to the current user.", 403);

        int leaveYear = ResolveLeaveYear(year);

        var entries = await _dbContext.LeaveLedgerEntries
            .AsNoTracking()
            .Where(l => l.EmployeeId == employee.Id
                        && l.LeaveTypeId == leaveTypeId
                        && l.LeaveYear == leaveYear)
            .OrderBy(l => l.OccurredAt)
            .ThenBy(l => l.CreatedAt)
            .Select(l => new LeaveLedgerEntryDto
            {
                Id = l.Id,
                LeaveTypeId = l.LeaveTypeId,
                LeaveYear = l.LeaveYear,
                EntryType = l.EntryType.ToString(),
                Amount = l.Amount,
                BalanceAfter = l.BalanceAfter,
                Description = l.Description,
                LeaveRequestId = l.LeaveRequestId,
                OccurredAt = l.OccurredAt,
            })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<LeaveLedgerEntryDto>>.Success(entries);
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-4, AC-3: upcoming leaves
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<UpcomingLeaveDto>>> GetMyUpcomingAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<UpcomingLeaveDto>>.Failure("Tenant context is not resolved.", 400);

        var employee = await GetCurrentEmployeeAsync(cancellationToken);
        if (employee is null)
            return Result<IReadOnlyList<UpcomingLeaveDto>>.Failure(
                "No employee record is linked to the current user.", 403);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var upcoming = await _dbContext.LeaveRequests
            .AsNoTracking()
            .Include(lr => lr.LeaveType)
            .Where(lr => lr.EmployeeId == employee.Id
                         && lr.StartDate >= today
                         && (lr.Status == LeaveRequestStatus.Approved
                             || lr.Status == LeaveRequestStatus.Pending))
            .OrderBy(lr => lr.StartDate)
            .ThenBy(lr => lr.EndDate)
            .ToListAsync(cancellationToken);

        var dtos = upcoming.Select(lr => new UpcomingLeaveDto
        {
            RequestId = lr.Id,
            LeaveTypeId = lr.LeaveTypeId,
            LeaveTypeName = lr.LeaveType?.Name ?? string.Empty,
            LeaveTypeColor = lr.LeaveType?.Color,
            StartDate = lr.StartDate,
            EndDate = lr.EndDate,
            TotalDays = lr.TotalDays,
            IsHalfDay = lr.IsHalfDay,
            HalfDaySession = lr.HalfDaySession,
            Status = lr.Status.ToString(),
        }).ToList();

        return Result<IReadOnlyList<UpcomingLeaveDto>>.Success(dtos);
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// BR-4: resolve the leave year. No tenant fiscal-year config entity exists in the codebase,
    /// so the established calendar-year convention (US-LV-002..005) is reused. The supplied year
    /// (BR-5 previous-year selector) is used verbatim when provided.
    /// TODO(tenant-settings): when a tenant leave-year config exists, derive the year boundary from
    ///   it instead of the calendar year.
    /// </summary>
    private static int ResolveLeaveYear(int? year)
        => year ?? DateTime.UtcNow.Year;

    private async Task<decimal> ResolveEntitlementAsync(
        Guid employeeId, Guid leaveTypeId, int leaveYear, CancellationToken cancellationToken)
    {
        var entitlement = await _entitlementService.ComputeEffectiveEntitlementAsync(
            employeeId, leaveTypeId, leaveYear, cancellationToken);

        // The engine returns probation/ineligible cases as 0 (never a failure); on the rare
        // failure path (e.g. missing reference data) fall back to 0 so the dashboard still renders.
        return entitlement.IsSuccess ? entitlement.Value!.ProratedEntitlementDays : 0m;
    }

    private Task<Employee?> GetCurrentEmployeeAsync(CancellationToken cancellationToken)
        => _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);

    /// <summary>
    /// Component sums of a single leave type's ledger entries (BR-1). Used/Expired are exposed as
    /// positive magnitudes (the ledger stores them as negative Amounts); Adjustments are signed.
    /// </summary>
    private readonly record struct LedgerComponents(
        decimal Used, decimal CarryForward, decimal Expired, decimal Adjustments)
    {
        public static readonly LedgerComponents Empty = new(0m, 0m, 0m, 0m);

        public static LedgerComponents From(IEnumerable<(LedgerEntryType EntryType, decimal Amount)> entries)
        {
            decimal used = 0m, carry = 0m, expired = 0m, adjusted = 0m;
            foreach (var (type, amount) in entries)
            {
                switch (type)
                {
                    case LedgerEntryType.Used:
                    case LedgerEntryType.Encashed:
                        // Stored as negative; surface as positive consumed magnitude.
                        used += Math.Abs(amount);
                        break;
                    case LedgerEntryType.CarryForward:
                        carry += amount;
                        break;
                    case LedgerEntryType.Expired:
                        expired += Math.Abs(amount);
                        break;
                    case LedgerEntryType.Adjusted:
                        adjusted += amount; // signed
                        break;
                    case LedgerEntryType.Accrual:
                    default:
                        // Accrual realises the entitlement, which is sourced from the engine value;
                        // not re-added here to avoid double counting against the engine entitlement.
                        break;
                }
            }
            return new LedgerComponents(used, carry, expired, adjusted);
        }
    }
}
