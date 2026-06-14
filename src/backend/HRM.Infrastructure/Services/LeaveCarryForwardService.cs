using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveCarryForward.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Leave carry-forward and expiry processing (US-LV-008). Implements the two Hangfire jobs'
/// per-tenant work and the read-only preview. All queries are tenant-scoped via ITenantContext
/// + EF global query filters (the Hangfire jobs set the tenant context per tenant before calling
/// in, mirroring LeaveAccrualJob / HolidayRecurrenceJob).
///
/// Shared math: the year-end job and the preview both call into the pure
/// <see cref="LeaveCarryForwardCalculator"/> so they cannot diverge (the "preview must match the
/// job" Test Hint).
///
/// Balance math (unused_balance for the closing year) reuses the US-LV-006 component-wise ledger
/// formula: Balance = Entitlement + CarryForward − Used − Expired + Adjustments, where Entitlement
/// is resolved by the US-LV-002 engine via ILeaveEntitlementService.
///
/// DEFERRALS (seams only — see vault "Carry-Forward &amp; Expiry (US-LV-008)"):
///   - Redis cache invalidation after processing (FR-7/AC-3) — consistent with the module-wide
///     deferred-Redis decision. TODO(redis-balance-cache).
///   - Tenant fiscal-year boundary (§10) — calendar year reused. TODO(tenant-settings).
/// </summary>
public sealed class LeaveCarryForwardService : ILeaveCarryForwardService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILeaveEntitlementService _entitlementService;
    private readonly ILogger<LeaveCarryForwardService> _logger;

    /// <summary>Batch size for year-end processing (NFR-1: handle 5,000 employees).</summary>
    private const int BatchSize = 500;

    public LeaveCarryForwardService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILeaveEntitlementService entitlementService,
        ILogger<LeaveCarryForwardService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _entitlementService = entitlementService;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-2, AC-1/AC-2/AC-4, BR-1/BR-2/BR-5/BR-6: year-end carry-forward
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<int>> ProcessYearEndAsync(
        int fromYear, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<int>.Failure("Tenant context is not resolved.", 400);

        int toYear = fromYear + 1;

        // BR-6: only leave types where carry-forward applies.
        var leaveTypes = (await _dbContext.LeaveTypes
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Where(LeaveCarryForwardCalculator.AppliesTo)
            .ToList();

        if (leaveTypes.Count == 0)
        {
            _logger.LogInformation(
                "LeaveCarryForward year-end: no carry-forward-applicable leave types for tenant {TenantId}, year {Year}.",
                _tenantContext.TenantId, fromYear);
            return Result<int>.Success(0);
        }

        int created = 0;
        int skip = 0;
        int totalEmployees = await _dbContext.Employees
            .CountAsync(e => e.IsActive && !e.IsDeleted, cancellationToken);

        while (skip < totalEmployees)
        {
            var employees = await _dbContext.Employees
                .AsNoTracking()
                .Where(e => e.IsActive && !e.IsDeleted)
                .OrderBy(e => e.Id)
                .Skip(skip)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (employees.Count == 0)
                break;

            foreach (var employee in employees)
            {
                foreach (var leaveType in leaveTypes)
                {
                    if (await ProcessSinglePairAsync(employee, leaveType, fromYear, toYear, cancellationToken))
                        created++;
                }
            }

            skip += BatchSize;
        }

        // TODO(redis-balance-cache): invalidate cached balances for processed employees here
        //   (FR-7) once a tenant-scoped balance cache exists.

        _logger.LogInformation(
            "LeaveCarryForward year-end complete for tenant {TenantId}: {Count} carry-forward rows created (year {From}->{To}).",
            _tenantContext.TenantId, created, fromYear, toYear);

        return Result<int>.Success(created);
    }

    /// <summary>
    /// Processes one employee × leave type. Returns true if a carry-forward tracking row was
    /// created (i.e. work was done), false if skipped for idempotency.
    /// </summary>
    private async Task<bool> ProcessSinglePairAsync(
        Employee employee, LeaveType leaveType, int fromYear, int toYear,
        CancellationToken cancellationToken)
    {
        // NFR-3 idempotency: skip if a tracking row already exists for this pair/year transition.
        bool alreadyTracked = await _dbContext.LeaveCarryForwardTrackings
            .AnyAsync(t => t.EmployeeId == employee.Id
                           && t.LeaveTypeId == leaveType.Id
                           && t.FromYear == fromYear
                           && t.ToYear == toYear,
                cancellationToken);
        if (alreadyTracked)
            return false;

        decimal unused = await ComputeUnusedBalanceAsync(employee.Id, leaveType, fromYear, cancellationToken);

        var outcome = LeaveCarryForwardCalculator.Compute(unused, leaveType.CarryForwardLimit!.Value);

        // Nothing to carry or forfeit — still record a (zero) tracking row so the job is idempotent
        // and the preview/expiry have a stable anchor. But avoid creating noise ledger entries.
        DateOnly? expiryDate = LeaveCarryForwardCalculator.ComputeExpiryDate(
            toYear, leaveType.CarryForwardExpiryMonths);

        var newYearStart = new DateOnly(toYear, 1, 1);
        var newYearStartUtc = new DateTime(toYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEndUtc = new DateTime(fromYear, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        // ── Carry-forward ledger entry (positive, new leave year) — FR-6 ──
        if (outcome.CarriedDays > 0m)
        {
            await AppendLedgerAsync(
                employee, leaveType.Id, toYear, LedgerEntryType.CarryForward, outcome.CarriedDays,
                $"Carry-forward from {fromYear}: {outcome.CarriedDays} days", newYearStartUtc, cancellationToken);
        }

        // ── Forfeiture: encash (BR-5) or expire (BR-2/AC-4) ──
        if (outcome.ForfeitedDays > 0m)
        {
            if (leaveType.Encashable)
            {
                // BR-5: encashable leave types may encash the forfeitable balance instead of expiring it.
                // Cap at MaxEncashDays when configured; any residue beyond the cap still expires.
                decimal encashable = leaveType.MaxEncashDays.HasValue
                    ? Math.Min(outcome.ForfeitedDays, leaveType.MaxEncashDays.Value)
                    : outcome.ForfeitedDays;

                if (encashable > 0m)
                {
                    await AppendLedgerAsync(
                        employee, leaveType.Id, fromYear, LedgerEntryType.Encashed, -encashable,
                        $"Encashment of forfeitable balance at {fromYear} year-end: {encashable} days",
                        yearEndUtc, cancellationToken);
                }

                decimal residual = outcome.ForfeitedDays - encashable;
                if (residual > 0m)
                {
                    await AppendLedgerAsync(
                        employee, leaveType.Id, fromYear, LedgerEntryType.Expired, -residual,
                        $"Forfeiture at {fromYear} year-end (over encashment cap): {residual} days",
                        yearEndUtc, cancellationToken);
                }
            }
            else
            {
                await AppendLedgerAsync(
                    employee, leaveType.Id, fromYear, LedgerEntryType.Expired, -outcome.ForfeitedDays,
                    $"Forfeiture at {fromYear} year-end: {outcome.ForfeitedDays} days",
                    yearEndUtc, cancellationToken);
            }
        }

        // ── Tracking row (FR-3/BR-4 anchor + NFR-3 idempotency) ──
        var tracking = new LeaveCarryForwardTracking
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = employee.TenantId,
            EmployeeId = employee.Id,
            LeaveTypeId = leaveType.Id,
            FromYear = fromYear,
            ToYear = toYear,
            CarriedDays = outcome.CarriedDays,
            ExpiryDate = outcome.CarriedDays > 0m ? expiryDate : null,
            ExpiredDays = 0m,
            Status = outcome.CarriedDays > 0m
                ? CarryForwardTrackingStatus.Active
                : CarryForwardTrackingStatus.Consumed, // nothing carried -> nothing left to expire
        };
        _dbContext.LeaveCarryForwardTrackings.Add(tracking);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-3, AC-3, BR-3, BR-4: monthly carry-forward expiry
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<int>> ProcessExpiryAsync(
        DateOnly asOf, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<int>.Failure("Tenant context is not resolved.", 400);

        // Active rows whose expiry date has passed (idempotent: Expired rows are not re-processed).
        var dueRows = await _dbContext.LeaveCarryForwardTrackings
            .Where(t => t.Status == CarryForwardTrackingStatus.Active
                        && t.ExpiryDate != null
                        && t.ExpiryDate <= asOf)
            .ToListAsync(cancellationToken);

        if (dueRows.Count == 0)
            return Result<int>.Success(0);

        int expired = 0;

        foreach (var row in dueRows)
        {
            // BR-4 FIFO: how many carried days remain after new-year consumption + prior expiry.
            decimal usedInNewYear = await GetUsedInYearAsync(
                row.EmployeeId, row.LeaveTypeId, row.ToYear, cancellationToken);

            decimal remaining = LeaveCarryForwardCalculator.RemainingCarriedDays(
                row.CarriedDays, usedInNewYear, row.ExpiredDays);

            if (remaining > 0m)
            {
                var employee = await _dbContext.Employees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Id == row.EmployeeId, cancellationToken);
                if (employee is null)
                    continue;

                var occurredAt = row.ExpiryDate!.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                await AppendLedgerAsync(
                    employee, row.LeaveTypeId, row.ToYear, LedgerEntryType.Expired, -remaining,
                    $"Carry-forward expiry on {row.ExpiryDate:yyyy-MM-dd}: {remaining} days",
                    occurredAt, cancellationToken);

                row.ExpiredDays += remaining;
            }

            // Mark the row terminal so it is never re-processed (idempotent), whether it expired
            // days or was already fully consumed by FIFO usage.
            row.Status = remaining > 0m
                ? CarryForwardTrackingStatus.Expired
                : CarryForwardTrackingStatus.Consumed;

            await _dbContext.SaveChangesAsync(cancellationToken);
            if (remaining > 0m)
                expired++;
        }

        // TODO(redis-balance-cache): invalidate cached balances for affected employees (AC-3/FR-7).

        _logger.LogInformation(
            "LeaveCarryForward expiry complete for tenant {TenantId} as of {AsOf}: {Count} tracking rows expired.",
            _tenantContext.TenantId, asOf, expired);

        return Result<int>.Success(expired);
    }

    // ══════════════════════════════════════════════════════════════
    //  FR-5, AC-5: read-only preview (shares ProcessYearEnd's calculation)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<CarryForwardPreviewDto>>> PreviewYearEndAsync(
        int fromYear, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<CarryForwardPreviewDto>>.Failure("Tenant context is not resolved.", 400);

        int toYear = fromYear + 1;

        var leaveTypes = (await _dbContext.LeaveTypes
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .Where(LeaveCarryForwardCalculator.AppliesTo)
            .ToList();

        var employees = await _dbContext.Employees
            .AsNoTracking()
            .Where(e => e.IsActive && !e.IsDeleted)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(cancellationToken);

        var result = new List<CarryForwardPreviewDto>();

        foreach (var employee in employees)
        {
            foreach (var leaveType in leaveTypes)
            {
                decimal unused = await ComputeUnusedBalanceAsync(
                    employee.Id, leaveType, fromYear, cancellationToken);

                var outcome = LeaveCarryForwardCalculator.Compute(unused, leaveType.CarryForwardLimit!.Value);

                // Only surface rows with something to carry or forfeit — keeps the preview report
                // focused on actionable employees (matches the job's "did work" rows).
                if (outcome.CarriedDays == 0m && outcome.ForfeitedDays == 0m)
                    continue;

                result.Add(new CarryForwardPreviewDto
                {
                    EmployeeId = employee.Id,
                    EmployeeName = $"{employee.FirstName} {employee.LastName}".Trim(),
                    EmployeeNo = employee.EmployeeNo,
                    LeaveTypeId = leaveType.Id,
                    LeaveTypeName = leaveType.Name,
                    FromYear = fromYear,
                    ToYear = toYear,
                    UnusedBalance = outcome.UnusedBalance,
                    CarryForward = outcome.CarriedDays,
                    Forfeited = outcome.ForfeitedDays,
                    WouldEncash = leaveType.Encashable && outcome.ForfeitedDays > 0m,
                });
            }
        }

        return Result<IReadOnlyList<CarryForwardPreviewDto>>.Success(result);
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes the closing-year unused balance for one employee × leave type, reusing the
    /// US-LV-006 component-wise formula: Entitlement (from the US-LV-002 engine) + CarryForward
    /// − Used − Expired + Adjustments. Used folds in Encashed (both are consumption).
    /// </summary>
    private async Task<decimal> ComputeUnusedBalanceAsync(
        Guid employeeId, LeaveType leaveType, int leaveYear, CancellationToken cancellationToken)
    {
        var ledger = await _dbContext.LeaveLedgerEntries
            .AsNoTracking()
            .Where(l => l.EmployeeId == employeeId
                        && l.LeaveTypeId == leaveType.Id
                        && l.LeaveYear == leaveYear)
            .Select(l => new { l.EntryType, l.Amount })
            .ToListAsync(cancellationToken);

        decimal used = 0m, carry = 0m, expired = 0m, adjusted = 0m;
        foreach (var e in ledger)
        {
            switch (e.EntryType)
            {
                case LedgerEntryType.Used:
                case LedgerEntryType.Encashed:
                    used += Math.Abs(e.Amount);
                    break;
                case LedgerEntryType.CarryForward:
                    carry += e.Amount;
                    break;
                case LedgerEntryType.Expired:
                    expired += Math.Abs(e.Amount);
                    break;
                case LedgerEntryType.Adjusted:
                    adjusted += e.Amount; // signed
                    break;
                // Accrual realises the engine entitlement; not re-added (avoids double count).
            }
        }

        decimal entitlement = await ResolveEntitlementAsync(employeeId, leaveType.Id, leaveYear, cancellationToken);

        return entitlement + carry - used - expired + adjusted;
    }

    private async Task<decimal> ResolveEntitlementAsync(
        Guid employeeId, Guid leaveTypeId, int leaveYear, CancellationToken cancellationToken)
    {
        var entitlement = await _entitlementService.ComputeEffectiveEntitlementAsync(
            employeeId, leaveTypeId, leaveYear, cancellationToken);
        return entitlement.IsSuccess ? entitlement.Value!.ProratedEntitlementDays : 0m;
    }

    /// <summary>Total days used (incl. encashed) in a leave year for FIFO derivation (BR-4).</summary>
    private async Task<decimal> GetUsedInYearAsync(
        Guid employeeId, Guid leaveTypeId, int leaveYear, CancellationToken cancellationToken)
    {
        var amounts = await _dbContext.LeaveLedgerEntries
            .AsNoTracking()
            .Where(l => l.EmployeeId == employeeId
                        && l.LeaveTypeId == leaveTypeId
                        && l.LeaveYear == leaveYear
                        && (l.EntryType == LedgerEntryType.Used || l.EntryType == LedgerEntryType.Encashed))
            .Select(l => l.Amount)
            .ToListAsync(cancellationToken);

        return amounts.Sum(Math.Abs);
    }

    /// <summary>
    /// Appends a ledger entry with a correctly-chained BalanceAfter running total. Does NOT call
    /// SaveChanges (callers batch saves) — but reads the running balance from the DB, so callers
    /// that append multiple entries for the same key must save between them; we keep it simple by
    /// having each entry compute against the persisted balance and saving per logical operation.
    /// </summary>
    private async Task AppendLedgerAsync(
        Employee employee, Guid leaveTypeId, int leaveYear, LedgerEntryType entryType,
        decimal amount, string description, DateTime occurredAt, CancellationToken cancellationToken)
    {
        decimal currentBalance = await GetLedgerBalanceAsync(
            employee.Id, leaveTypeId, leaveYear, cancellationToken);

        var entry = new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = employee.TenantId,
            EntryType = entryType,
            EmployeeId = employee.Id,
            LeaveTypeId = leaveTypeId,
            LeaveYear = leaveYear,
            Amount = amount,
            BalanceAfter = currentBalance + amount,
            Description = description,
            OccurredAt = occurredAt,
        };

        _dbContext.LeaveLedgerEntries.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<decimal> GetLedgerBalanceAsync(
        Guid employeeId, Guid leaveTypeId, int leaveYear, CancellationToken cancellationToken)
    {
        var lastEntry = await _dbContext.LeaveLedgerEntries
            .AsNoTracking()
            .Where(l => l.EmployeeId == employeeId
                        && l.LeaveTypeId == leaveTypeId
                        && l.LeaveYear == leaveYear)
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return lastEntry?.BalanceAfter ?? 0m;
    }
}
