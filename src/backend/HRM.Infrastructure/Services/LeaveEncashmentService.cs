using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// HR-triggered leave encashment (US-PAY-010 AC-3/FR-5). Computes the encashment amount from the employee's
/// CURRENT monthly BASIC and the period working-days (<c>daily_rate = monthly_basic / working_days</c>;
/// <c>amount = eligible_days * daily_rate</c>), then creates an EARNING adjustment (a Bonus) for the next
/// payroll run by REUSING the US-PAY-007 <see cref="IPayrollAdjustmentService"/> — so the encashment flows into
/// the next run exactly like any other bonus (and BR-7/BR-8 period-deferral is handled there). The adjustment's
/// description carries the <see cref="PayrollRunProcessor.EncashmentDescriptionPrefix"/> + day count so the run
/// engine recognises it and stamps <c>leave_encashment_days/amount</c> on the slip.
///
/// <para>BR-6 (encashment eligibility, BUG-079): when a leave type is supplied, the type must have
/// <see cref="Domain.Entities.LeaveType.Encashable"/> = true AND the requested days must not exceed the
/// employee's <b>forfeitable</b> balance — the portion of the current leave-ledger balance that lies over the
/// type's <c>CarryForwardLimit</c> (the only part the year-end job would otherwise auto-encash). This ceiling is
/// computed with the SAME authoritative <see cref="LeaveCarryForwardCalculator"/> the year-end job uses, then the
/// paid days are still capped at the type's <c>MaxEncashDays</c>. An over-balance request is REJECTED (422
/// <c>encashment_exceeds_balance</c>) — HR must see it, not have it silently clipped.</para>
///
/// <para>BUG-079 double-pay fix: on acceptance the service writes an <see cref="LedgerEntryType.Encashed"/>
/// ledger draw-down (mirroring the year-end auto-encashment in <see cref="LeaveCarryForwardService"/>) in the SAME
/// unit of work as the payroll adjustment. Because the year-end job recomputes the forfeitable balance from the
/// current ledger (folding <c>Encashed</c> entries into consumption), the drawn-down days are excluded next
/// year-end — the same days can no longer be paid twice. LeaveTypeId == null is the HR manual-override branch:
/// it skips the type/balance gate AND the ledger draw-down (existing design — no leave type to draw against).
/// Tenant-scoped via ITenantContext + the EF global query filter (AC-5/FR-8).</para>
/// </summary>
public sealed class LeaveEncashmentService : ILeaveEncashmentService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPayrollAdjustmentService _adjustmentService;
    private readonly ITenantLeaveYearResolver? _leaveYearResolver;
    private readonly ILogger<LeaveEncashmentService> _logger;

    public LeaveEncashmentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IPayrollAdjustmentService adjustmentService,
        ILogger<LeaveEncashmentService> logger,
        ITenantLeaveYearResolver? leaveYearResolver = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _adjustmentService = adjustmentService;
        // ISSUE-305: trailing-optional so existing payroll fixtures keep compiling; DI always supplies it.
        // Absent => the pay year, i.e. the pre-ISSUE-305 behaviour (correct for a calendar tenant).
        _leaveYearResolver = leaveYearResolver;
        _logger = logger;
    }

    public async Task<Result<LeaveEncashmentResultDto>> ProcessAsync(
        LeaveEncashmentInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveEncashmentResultDto>.Failure("Tenant context is not resolved.", 400);
        if (input.PayMonth is < 1 or > 12)
            return Result<LeaveEncashmentResultDto>.Failure("Pay month must be between 1 and 12.", 400, "invalid_month");
        if (input.EligibleDays <= 0m)
            return Result<LeaveEncashmentResultDto>.Failure("Eligible days must be greater than zero.", 400, "invalid_eligible_days");

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == input.EmployeeId, cancellationToken);
        if (employee is null)
            return Result<LeaveEncashmentResultDto>.Failure("Employee not found.", 404, "employee_not_found");

        // BR-6 (BUG-079): validate encashment eligibility + cap when a leave type is supplied.
        var eligibleDays = input.EligibleDays;
        // ISSUE-305: the leave year the balance/draw-down apply to. This was `input.PayYear` — the PAYROLL
        // year, which is only the leave year for a calendar tenant. The ledger is keyed by the tenant's own
        // leave year, so for an Apr-Mar tenant a Jan-Mar pay period read a leave year that had not started:
        // availableBalance 0 => forfeitableCeiling 0 => encashment rejected outright, or (via F&F) a silent
        // under-payment on termination. Resolved from the pay PERIOD, not the pay year, so the draw-down hits
        // the same bucket the accrual credited.
        var leaveYear = _leaveYearResolver is null
            ? input.PayYear
            : await _leaveYearResolver.LabelForAsync(
                new DateOnly(input.PayYear, input.PayMonth, 1), cancellationToken);
        LeaveType? encashLeaveType = null;          // non-null => write a ledger draw-down (Part 2).
        decimal availableBalance = 0m;              // the ledger balance the draw-down chains from.
        if (input.LeaveTypeId is { } leaveTypeId)
        {
            var leaveType = await _dbContext.LeaveTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == leaveTypeId, cancellationToken);
            if (leaveType is null)
                return Result<LeaveEncashmentResultDto>.Failure("Leave type not found.", 404, "leave_type_not_found");
            if (!leaveType.Encashable)
                return Result<LeaveEncashmentResultDto>.Failure(
                    "This leave type is not eligible for encashment.", 422, "leave_type_not_encashable");

            // BR-6 balance/carry-forward gate: encashment is only for the balance that EXCEEDS the carry-forward
            // limit (the "forfeitable" portion — the only part the year-end job would auto-encash). Read the
            // employee's current available balance from the ledger, then compute the forfeitable ceiling with the
            // SAME authoritative calculator the year-end job uses so the two paths agree on what is encashable.
            availableBalance = await GetLedgerBalanceAsync(input.EmployeeId, leaveTypeId, leaveYear, cancellationToken);
            decimal forfeitableCeiling = leaveType.CarryForwardLimit is { } carryForwardLimit
                ? LeaveCarryForwardCalculator.Compute(availableBalance, carryForwardLimit).ForfeitedDays
                : Math.Max(availableBalance, 0m); // no carry-forward limit configured => whole non-negative balance is encashable.

            if (input.EligibleDays > forfeitableCeiling)
                return Result<LeaveEncashmentResultDto>.Failure(
                    $"Requested {input.EligibleDays:0.##} encashment day(s) exceed the available forfeitable balance of " +
                    $"{forfeitableCeiling:0.##} day(s) (balance over the carry-forward limit).",
                    422, "encashment_exceeds_balance");

            if (leaveType.MaxEncashDays is { } max && eligibleDays > max)
                eligibleDays = max; // BR-6: cap the PAID days at the type's maximum encashable days.

            encashLeaveType = leaveType;
        }

        // daily_rate = monthly_basic / working_days (FR-5). working_days = scheduled days in the target month.
        var monthlyBasic = await CurrentMonthlyBasicAsync(input.EmployeeId, cancellationToken);
        if (monthlyBasic <= 0m)
            return Result<LeaveEncashmentResultDto>.Failure(
                "Employee has no current BASIC salary to derive a daily rate.", 422, "no_active_salary");

        var workingDays = await WorkingDaysInMonthAsync(input.EmployeeId, input.PayYear, input.PayMonth, cancellationToken);
        var dailyRate = Math.Round(monthlyBasic / workingDays, 2, MidpointRounding.AwayFromZero);
        var amount = Math.Round(eligibleDays * dailyRate, 2, MidpointRounding.AwayFromZero);

        // Part 2 (BUG-079) — ledger draw-down: reduce the leave balance by the encashed days so the year-end
        // auto-encashment (LeaveCarryForwardService) recomputes forfeitable on the REDUCED balance and cannot pay
        // the same days again. Mirror the year-end Encashed entry shape. It is ADDED to the shared scoped
        // DbContext but NOT saved here — the adjustment service's SaveChanges (same scoped context) persists BOTH
        // the adjustment and this draw-down in ONE unit of work, so the encashment + draw-down are atomic.
        PooledDeductionResult? drawDown = null;
        if (encashLeaveType is not null)
        {
            // DF-19 / ISSUE-045: draw the balance down FIFO — carry-forward bucket first, then accrual —
            // and bump the bucket's ConsumedDays so the year-end expiry job (which reads that counter)
            // accounts for encashed carried days instead of over-forfeiting them. Same shared split the
            // approval/LOP paths use; nets to −eligibleDays with the historical single-row aggregate.
            drawDown = await PooledLeaveLedger.AppendDeductionAsync(
                _dbContext, employee.TenantId, input.EmployeeId, encashLeaveType.Id, leaveYear,
                eligibleDays, leaveRequestId: null, availableBalance,
                $"HR leave encashment: {eligibleDays:0.##} day(s) for {input.PayYear}-{input.PayMonth:00} payroll",
                DateTime.UtcNow, LedgerEntryType.Encashed, cancellationToken);
        }

        // AC-3/FR-5: create the EARNING adjustment (Bonus) for the next payroll run, reusing US-PAY-007. The
        // description carries the encashment prefix + "(N days)" so the run engine recognises it and stamps the
        // slip's leave_encashment_days/amount. BR-7/BR-8 period deferral is handled inside the adjustment service.
        var description = $"{PayrollRunProcessor.EncashmentDescriptionPrefix}: {eligibleDays:0.##} day(s) @ {dailyRate:0.##}/day ({eligibleDays:0.##} days)";
        var createResult = await _adjustmentService.CreateAsync(
            new CreateAdjustmentInput(
                EmployeeId: input.EmployeeId,
                AdjustmentType: nameof(AdjustmentType.Bonus),
                Amount: amount,
                Description: description,
                ApplicablePayMonth: input.PayMonth,
                ApplicablePayYear: input.PayYear,
                IsTaxable: input.IsTaxable,
                IsRecurring: false,
                RecurrenceEndMonth: null,
                RecurrenceEndYear: null,
                ReferencePayrollSlipId: null),
            cancellationToken);

        if (createResult.IsFailure)
        {
            // Adjustment rejected => do NOT persist the draw-down (both-or-neither). The rows were only
            // tracked, never saved; detach them AND revert the in-memory ConsumedDays bump so an unrelated
            // later SaveChanges in this scope cannot flush an orphan or a phantom consumption.
            drawDown?.Undo(_dbContext);
            return Result<LeaveEncashmentResultDto>.Failure(
                createResult.Error!, createResult.StatusCode ?? 400, createResult.ErrorCode);
        }

        var created = createResult.Value!;
        _logger.LogInformation(
            "Leave encashment processed. Employee={Employee}, Days={Days}, Amount={Amount}, Period={Year}-{Month}, Tenant={Tenant}",
            input.EmployeeId, eligibleDays, amount, created.Adjustment.ApplicablePayYear,
            created.Adjustment.ApplicablePayMonth, _tenantContext.TenantId);

        return Result<LeaveEncashmentResultDto>.Success(new LeaveEncashmentResultDto
        {
            AdjustmentId = created.Adjustment.Id,
            EmployeeId = input.EmployeeId,
            EncashedDays = eligibleDays,
            DailyRate = dailyRate,
            Amount = amount,
            PayMonth = created.Adjustment.ApplicablePayMonth,
            PayYear = created.Adjustment.ApplicablePayYear,
            PeriodDeferred = created.DeferredToPayMonth is not null,
        });
    }

    /// <summary>
    /// The employee's current available leave balance for a type/year = the <c>BalanceAfter</c> of the most recent
    /// ledger entry (replicates <c>LeaveRequestService.GetLedgerBalanceAsync</c> — the US-LV-002 balance source).
    /// No ledger entries => balance 0. Tenant-scoped via the EF global query filter.
    /// </summary>
    private async Task<decimal> GetLedgerBalanceAsync(
        Guid employeeId, Guid leaveTypeId, int leaveYear, CancellationToken ct)
    {
        var lastEntry = await _dbContext.LeaveLedgerEntries
            .AsNoTracking()
            .Where(l => l.EmployeeId == employeeId
                        && l.LeaveTypeId == leaveTypeId
                        && l.LeaveYear == leaveYear)
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(ct);

        return lastEntry?.BalanceAfter ?? 0m;
    }

    /// <summary>The employee's current effective monthly BASIC (the encashment daily-rate base).</summary>
    private async Task<decimal> CurrentMonthlyBasicAsync(Guid employeeId, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var basic = await (
            from c in _dbContext.EmployeeSalaryComponents.AsNoTracking()
            join comp in _dbContext.SalaryComponents.AsNoTracking() on c.SalaryComponentId equals comp.Id
            where c.EmployeeId == employeeId
                && c.EffectiveFrom <= today
                && (c.EffectiveTo == null || c.EffectiveTo >= today)
                && comp.Code == "BASIC"
            select (decimal?)c.MonthlyAmount).FirstOrDefaultAsync(ct);
        return basic ?? 0m;
    }

    /// <summary>
    /// ISSUE-180: the scheduled working-day denominator for the encashment daily rate — the SHIFT working-days
    /// in the target month for this employee, resolved via the SAME <see cref="ShiftScheduleResolver"/> the
    /// payroll run uses for its working-days figure, so <c>daily_rate = monthly_basic / working_days</c> matches
    /// the run exactly (the run's golden 22000/22 = 1000/day). An employee with no resolvable shift falls back
    /// to counting every calendar day (the resolver's empty-set rule) — identical to the former behaviour.
    /// </summary>
    private async Task<int> WorkingDaysInMonthAsync(Guid employeeId, int year, int month, CancellationToken ct)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var sets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            _dbContext, new[] { employeeId }, start, ct);
        var count = ShiftScheduleResolver.CountWorkingDays(sets[employeeId], start, end);
        // Defensive: a shift with no working day in the month would divide by zero — fall back to calendar days.
        return count > 0 ? count : (end.DayNumber - start.DayNumber + 1);
    }
}
