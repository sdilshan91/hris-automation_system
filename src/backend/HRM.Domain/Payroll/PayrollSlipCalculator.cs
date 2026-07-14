using HRM.Domain.Enums;

namespace HRM.Domain.Payroll;

/// <summary>
/// One resolved monthly salary-component value for an employee, fed into the payroll engine
/// (US-PAY-003 FR-5a). Sourced from the employee's current <c>employee_salary_component</c> rows.
/// </summary>
/// <param name="ComponentId">Salary component id.</param>
/// <param name="Code">Component code (e.g. "BASIC").</param>
/// <param name="Name">Component display name (denormalized onto the slip detail).</param>
/// <param name="Type">Component classification — earnings add to gross, deductions/statutory reduce net.</param>
/// <param name="IsStatutory">Statutory flag from the component (BR — statutory amounts are summed for the run total).</param>
/// <param name="MonthlyAmount">Resolved monthly amount (annual/12) before pro-ration / LOP.</param>
/// <param name="ProcessingOrder">Order the component appears on the slip (lower first).</param>
public readonly record struct PayrollComponentInput(
    Guid ComponentId,
    string Code,
    string Name,
    SalaryComponentType Type,
    bool IsStatutory,
    decimal MonthlyAmount,
    int ProcessingOrder);

/// <summary>
/// All per-employee inputs the payroll engine needs to compute one slip (US-PAY-003 FR-5).
/// Pure data — assembled by the processor from salary + attendance data, then handed to the calculator.
/// </summary>
/// <param name="EmployeeId">Employee id.</param>
/// <param name="Components">Resolved monthly component values for the period.</param>
/// <param name="WorkingDays">Scheduled working days in the period (BR-2 denominator). Must be &gt; 0 for pay.</param>
/// <param name="LopDays">Loss-of-pay days = absent days without approved leave (BR-2).</param>
/// <param name="ProRataPaidDays">
/// When the employee joined or separated mid-month (BR-4/BR-5), the number of working days they were
/// actually employed in the period (the pay numerator before LOP). Null = full month (= WorkingDays).
/// </param>
public sealed record PayrollSlipInput(
    Guid EmployeeId,
    IReadOnlyList<PayrollComponentInput> Components,
    decimal WorkingDays,
    decimal LopDays,
    decimal? ProRataPaidDays);

/// <summary>A computed component line on a slip (US-PAY-003 FR-5f).</summary>
public readonly record struct PayrollSlipLine(
    Guid ComponentId,
    string Name,
    SalaryComponentType Type,
    bool IsStatutory,
    decimal Amount,
    string CalculationBasis);

/// <summary>The fully-computed result for one employee's payslip (US-PAY-003 FR-5).</summary>
/// <remarks>
/// TAX-3: <see cref="TaxableIncome"/> / <see cref="IncomeTaxWithheld"/> surface the income-tax basis out of the
/// statutory pass so the processor can persist them on every slip. They default to 0 (no income-tax rule /
/// skipped / stripped), so the pure calculator and every existing construction are unaffected.
/// </remarks>
public sealed record PayrollSlipResult(
    Guid EmployeeId,
    decimal GrossEarnings,
    decimal TotalDeductions,
    decimal NetSalary,
    decimal StatutoryTotal,
    decimal WorkingDays,
    decimal PaidDays,
    decimal LopDays,
    IReadOnlyList<PayrollSlipLine> Lines,
    decimal TaxableIncome = 0m,
    decimal IncomeTaxWithheld = 0m);

/// <summary>
/// Pure, deterministic payroll-slip compute engine (US-PAY-003 FR-5). No DB / tenant / clock access, so it
/// is fully unit-testable with golden datasets (NFR-5). Given an employee's resolved monthly components and
/// attendance basis it produces the per-component lines, LOP deduction, pro-ration, and the rolled-up
/// gross / total-deductions / net with consistent rounding + penny reconciliation (BR-8).
///
/// <para>Computation model:</para>
/// <list type="number">
///   <item>Each component's monthly amount is pro-rated by <c>paid_days / working_days</c> when the employee
///         joined/separated mid-month (BR-4/BR-5); a full-month employee is not pro-rated.</item>
///   <item>LOP (BR-2) is a deduction line: <c>lop_amount = monthly_basic / working_days * lop_days</c>,
///         where <c>monthly_basic</c> is the (already pro-rated) BASIC earning. LOP reduces net pay.</item>
///   <item>Gross = sum(earning + reimbursement lines). Deductions = sum(deduction + statutory lines) + LOP.
///         Net = gross - deductions.</item>
///   <item>BR-8: every line is rounded half-up to 2 dp; the largest-magnitude EARNING line absorbs any
///         penny residual so that sum(lines, signed) == declared net.</item>
/// </list>
/// </summary>
public static class PayrollSlipCalculator
{
    private const string BasicCode = "BASIC";
    private const string LopComponentName = "Loss of Pay";

    /// <summary>
    /// Computes one employee's payslip. <paramref name="lopComponentId"/> is the synthetic component id used
    /// for the LOP deduction line (so the slip detail carries a stable FK target; the engine itself does not
    /// require a real component for LOP). Returns the result; never throws on normal inputs.
    /// </summary>
    public static PayrollSlipResult Compute(PayrollSlipInput input, Guid lopComponentId)
    {
        var workingDays = input.WorkingDays;
        // Pro-ration factor for a mid-month joiner/leaver (BR-4/BR-5). Full month => 1.0.
        var paidDaysBeforeLop = input.ProRataPaidDays ?? workingDays;
        if (paidDaysBeforeLop > workingDays) paidDaysBeforeLop = workingDays;
        if (paidDaysBeforeLop < 0m) paidDaysBeforeLop = 0m;

        var proRataFactor = workingDays > 0m ? paidDaysBeforeLop / workingDays : 0m;

        var ordered = input.Components
            .OrderBy(c => c.ProcessingOrder)
            .ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new List<PayrollSlipLine>(ordered.Count + 1);

        // Pro-rated BASIC monthly amount, used as the LOP daily-rate basis (BR-2).
        decimal proRatedBasic = 0m;

        foreach (var c in ordered)
        {
            var proRated = Round(c.MonthlyAmount * proRataFactor);
            if (string.Equals(c.Code, BasicCode, StringComparison.OrdinalIgnoreCase))
                proRatedBasic = proRated;

            var basis = proRataFactor == 1m
                ? $"{c.MonthlyAmount:0.##}/month"
                : $"{c.MonthlyAmount:0.##} x {paidDaysBeforeLop:0.##}/{workingDays:0.##} days";

            lines.Add(new PayrollSlipLine(c.ComponentId, c.Name, c.Type, c.IsStatutory, proRated, basis));
        }

        // BR-2: LOP deduction line. daily_rate = monthly_basic / working_days; amount = daily_rate * lop_days.
        var lopDays = input.LopDays < 0m ? 0m : input.LopDays;
        decimal lopAmount = 0m;
        if (lopDays > 0m && workingDays > 0m)
        {
            var dailyRate = proRatedBasic / workingDays;
            lopAmount = Round(dailyRate * lopDays);
            if (lopAmount > 0m)
            {
                lines.Add(new PayrollSlipLine(
                    lopComponentId, LopComponentName, SalaryComponentType.Deduction, false,
                    lopAmount, $"{Round(dailyRate):0.##}/day x {lopDays:0.##} days"));
            }
        }

        // Roll up. Earnings + reimbursements add to gross; deductions + statutory reduce net.
        decimal gross = 0m, deductions = 0m, statutory = 0m;
        foreach (var l in lines)
        {
            switch (l.Type)
            {
                case SalaryComponentType.Earning:
                case SalaryComponentType.Reimbursement:
                    gross += l.Amount;
                    break;
                case SalaryComponentType.Deduction:
                    deductions += l.Amount;
                    break;
                case SalaryComponentType.Statutory:
                    deductions += l.Amount;
                    statutory += l.Amount;
                    break;
            }
        }

        gross = Round(gross);
        deductions = Round(deductions);
        var net = Round(gross - deductions);

        // BR-8 penny reconciliation: ensure sum(signed lines) == net exactly. Any residual (from
        // independent per-line rounding) is absorbed into the largest-magnitude EARNING line.
        net = Reconcile(lines, gross, deductions, net);

        var paidDays = Math.Max(0m, paidDaysBeforeLop - lopDays);

        return new PayrollSlipResult(
            input.EmployeeId, gross, deductions, net, Round(statutory),
            workingDays, Round(paidDays), Round(lopDays), lines);
    }

    /// <summary>
    /// BR-8: guarantees sum(signed component amounts) == net. Earnings/reimbursements are positive, deductions/
    /// statutory negative. If a residual exists, it is added to the largest EARNING line and gross/net adjust
    /// in lock-step so the slip always balances to the penny.
    /// </summary>
    private static decimal Reconcile(List<PayrollSlipLine> lines, decimal gross, decimal deductions, decimal net)
    {
        decimal signedSum = 0m;
        foreach (var l in lines)
            signedSum += l.Type is SalaryComponentType.Earning or SalaryComponentType.Reimbursement ? l.Amount : -l.Amount;

        var residual = Round(net - signedSum);
        if (residual == 0m)
            return net;

        // Absorb into the largest-magnitude earning line (most stable; keeps the residual invisible at scale).
        var idx = -1;
        decimal best = -1m;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Type == SalaryComponentType.Earning && lines[i].Amount > best)
            {
                best = lines[i].Amount;
                idx = i;
            }
        }

        if (idx >= 0)
        {
            var adjusted = lines[idx] with { Amount = Round(lines[idx].Amount + residual) };
            lines[idx] = adjusted;
        }
        // net is the authoritative declared figure; the line adjustment makes the detail sum match it.
        return net;
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
