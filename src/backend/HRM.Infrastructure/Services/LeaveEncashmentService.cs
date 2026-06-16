using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
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
/// <para>BR-6 (encashment eligibility): when a leave type is supplied, the type must have
/// <see cref="Domain.Entities.LeaveType.Encashable"/> = true and the eligible days are capped at the type's
/// <c>MaxEncashDays</c>. The carry-forward-limit "balance over limit" check is the LEAVE module's surface; the
/// caller supplies the eligible-days figure (the documented BR-6 gap — see the module note). Tenant-scoped via
/// ITenantContext + the EF global query filter (AC-5/FR-8).</para>
/// </summary>
public sealed class LeaveEncashmentService : ILeaveEncashmentService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPayrollAdjustmentService _adjustmentService;
    private readonly ILogger<LeaveEncashmentService> _logger;

    public LeaveEncashmentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IPayrollAdjustmentService adjustmentService,
        ILogger<LeaveEncashmentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _adjustmentService = adjustmentService;
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

        // BR-6: validate encashment eligibility + cap when a leave type is supplied.
        var eligibleDays = input.EligibleDays;
        if (input.LeaveTypeId is { } leaveTypeId)
        {
            var leaveType = await _dbContext.LeaveTypes.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == leaveTypeId, cancellationToken);
            if (leaveType is null)
                return Result<LeaveEncashmentResultDto>.Failure("Leave type not found.", 404, "leave_type_not_found");
            if (!leaveType.Encashable)
                return Result<LeaveEncashmentResultDto>.Failure(
                    "This leave type is not eligible for encashment.", 422, "leave_type_not_encashable");
            if (leaveType.MaxEncashDays is { } max && eligibleDays > max)
                eligibleDays = max; // BR-6: cap at the type's maximum encashable days.
        }

        // daily_rate = monthly_basic / working_days (FR-5). working_days = scheduled days in the target month.
        var monthlyBasic = await CurrentMonthlyBasicAsync(input.EmployeeId, cancellationToken);
        if (monthlyBasic <= 0m)
            return Result<LeaveEncashmentResultDto>.Failure(
                "Employee has no current BASIC salary to derive a daily rate.", 422, "no_active_salary");

        var workingDays = WorkingDaysInMonth(input.PayYear, input.PayMonth);
        var dailyRate = Math.Round(monthlyBasic / workingDays, 2, MidpointRounding.AwayFromZero);
        var amount = Math.Round(eligibleDays * dailyRate, 2, MidpointRounding.AwayFromZero);

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
            return Result<LeaveEncashmentResultDto>.Failure(
                createResult.Error!, createResult.StatusCode ?? 400, createResult.ErrorCode);

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

    /// <summary>Scheduled working-day baseline for the daily rate — calendar days in the month (BR-8 shift-calendar deferred).</summary>
    private static decimal WorkingDaysInMonth(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        return end.DayNumber - start.DayNumber + 1;
    }
}
