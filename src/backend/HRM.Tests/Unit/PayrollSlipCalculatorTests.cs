// ============================================================================
// US-PAY-003: Payroll-slip compute engine — unit tests (pure, no DB).
//
// Golden-dataset coverage of the core payroll math (NFR-5 >= 85% on the engine):
//   - GOLDEN: known salary + attendance + statutory => exact gross/deductions/net.
//   - BR-2 LOP: 3 absent days in a 22-working-day month.
//   - BR-4 pro-ration: mid-month joiner (15th of a 30-day month).
//   - BR-8 penny reconciliation: sum of signed component lines == declared net.
//   - Statutory total is summed (FR-8) and statutory amounts reduce net (FR-5e).
// ============================================================================

using FluentAssertions;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;

namespace HRM.Tests.Unit;

public sealed class PayrollSlipCalculatorTests
{
    private static readonly Guid LopId = Guid.Parse("00000000-0000-0000-0000-0000000010ce");

    private static PayrollComponentInput Comp(
        string code, SalaryComponentType type, decimal monthly, int order, bool statutory = false)
        => new(Guid.NewGuid(), code, code, type, statutory, monthly, order);

    // ── GOLDEN DATASET ──────────────────────────────────────────────────────
    // Monthly: BASIC 50,000 (earning) + HRA 20,000 (earning) + PF 6,000 (statutory) + TAX 4,000 (deduction).
    // Full month, no LOP. Gross = 70,000; Deductions = 10,000; Net = 60,000; Statutory = 6,000.
    [Fact]
    public void Compute_GoldenDataset_FullMonth_ProducesExactTotals()
    {
        var input = new PayrollSlipInput(
            Guid.NewGuid(),
            new[]
            {
                Comp("BASIC", SalaryComponentType.Earning, 50_000m, 1),
                Comp("HRA", SalaryComponentType.Earning, 20_000m, 2),
                Comp("PF", SalaryComponentType.Statutory, 6_000m, 3, statutory: true),
                Comp("TAX", SalaryComponentType.Deduction, 4_000m, 4),
            },
            WorkingDays: 22m, LopDays: 0m, ProRataPaidDays: null);

        var r = PayrollSlipCalculator.Compute(input, LopId);

        r.GrossEarnings.Should().Be(70_000m);
        r.TotalDeductions.Should().Be(10_000m);
        r.NetSalary.Should().Be(60_000m);
        r.StatutoryTotal.Should().Be(6_000m);
        r.PaidDays.Should().Be(22m);
        r.LopDays.Should().Be(0m);
        r.Lines.Should().HaveCount(4); // no LOP line when LOP days = 0.
    }

    // ── DF-37/ISSUE-280: the component Code is carried onto the computed line ──
    [Fact]
    [Trait("TC", "TC-PAY-011-15")]
    public void Compute_CarriesComponentCode_OntoTheSlipLine_DistinctFromName_DF37()
    {
        var basicId = Guid.NewGuid();
        var input = new PayrollSlipInput(
            Guid.NewGuid(),
            new[]
            {
                // A BASIC component whose display Name was renamed to "Base Pay" — Code stays "BASIC".
                new PayrollComponentInput(basicId, "BASIC", "Base Pay", SalaryComponentType.Earning, false, 50_000m, 1),
                Comp("HRA", SalaryComponentType.Earning, 20_000m, 2),
            },
            WorkingDays: 22m, LopDays: 0m, ProRataPaidDays: null);

        var r = PayrollSlipCalculator.Compute(input, LopId);

        var basicLine = r.Lines.Single(l => l.ComponentId == basicId);
        basicLine.Code.Should().Be("BASIC", "the stable Code is threaded onto the line, independent of the display Name");
        basicLine.Name.Should().Be("Base Pay");
    }

    // ── BR-2 LOP: 3 absent days in a 22-working-day month ───────────────────
    // BASIC 22,000/month → daily rate 1,000; LOP 3 days = 3,000 deducted.
    [Fact]
    public void Compute_Lop_ThreeDaysIn22DayMonth_DeductsDailyRateTimesLopDays()
    {
        var input = new PayrollSlipInput(
            Guid.NewGuid(),
            new[] { Comp("BASIC", SalaryComponentType.Earning, 22_000m, 1) },
            WorkingDays: 22m, LopDays: 3m, ProRataPaidDays: null);

        var r = PayrollSlipCalculator.Compute(input, LopId);

        var lop = r.Lines.Single(l => l.ComponentId == LopId);
        lop.Amount.Should().Be(3_000m);          // 22000/22 * 3
        lop.Type.Should().Be(SalaryComponentType.Deduction);
        r.GrossEarnings.Should().Be(22_000m);
        r.TotalDeductions.Should().Be(3_000m);
        r.NetSalary.Should().Be(19_000m);
        r.LopDays.Should().Be(3m);
        r.PaidDays.Should().Be(19m);             // 22 working - 3 LOP
    }

    // ── BR-4 pro-ration: mid-month joiner (15th of a 30-day month) ──────────
    // Joined on the 15th → employed 16 of 30 calendar days. Pro-rata paid days supplied by the processor;
    // here we feed paidDays directly to assert the engine's pro-ration of component amounts.
    [Fact]
    public void Compute_MidMonthJoiner_ProRatesComponentsByPaidDays()
    {
        // Working days 22; joiner present for ~11 of them (half-ish month). BASIC 44,000/month.
        var input = new PayrollSlipInput(
            Guid.NewGuid(),
            new[] { Comp("BASIC", SalaryComponentType.Earning, 44_000m, 1) },
            WorkingDays: 22m, LopDays: 0m, ProRataPaidDays: 11m);

        var r = PayrollSlipCalculator.Compute(input, LopId);

        // 44000 * 11/22 = 22000
        r.GrossEarnings.Should().Be(22_000m);
        r.NetSalary.Should().Be(22_000m);
        r.PaidDays.Should().Be(11m);
    }

    [Fact]
    public void Compute_JoinerAfterPeriod_ZeroPaidDays_YieldsZeroPay()
    {
        var input = new PayrollSlipInput(
            Guid.NewGuid(),
            new[] { Comp("BASIC", SalaryComponentType.Earning, 30_000m, 1) },
            WorkingDays: 22m, LopDays: 0m, ProRataPaidDays: 0m);

        var r = PayrollSlipCalculator.Compute(input, LopId);

        r.GrossEarnings.Should().Be(0m);
        r.NetSalary.Should().Be(0m);
        r.PaidDays.Should().Be(0m);
    }

    // ── BR-8 penny reconciliation: signed line sum == declared net ──────────
    // Percent-ish amounts that round independently; engine must reconcile so the detail sums to net.
    [Fact]
    public void Compute_PennyReconciliation_SignedLinesSumToNet()
    {
        var input = new PayrollSlipInput(
            Guid.NewGuid(),
            new[]
            {
                Comp("BASIC", SalaryComponentType.Earning, 33_333.33m, 1),
                Comp("HRA", SalaryComponentType.Earning, 16_666.67m, 2),
                Comp("PF", SalaryComponentType.Statutory, 4_000.005m, 3, statutory: true),
                Comp("TAX", SalaryComponentType.Deduction, 2_500.004m, 4),
            },
            // 19 of 22 working days to force pro-rata rounding on every line.
            WorkingDays: 22m, LopDays: 0m, ProRataPaidDays: 19m);

        var r = PayrollSlipCalculator.Compute(input, LopId);

        decimal signed = 0m;
        foreach (var l in r.Lines)
            signed += l.Type is SalaryComponentType.Earning or SalaryComponentType.Reimbursement ? l.Amount : -l.Amount;

        signed.Should().Be(r.NetSalary);                       // BR-8: detail sum == net, to the penny.
        r.NetSalary.Should().Be(r.GrossEarnings - r.TotalDeductions);
        // Every line is 2-dp.
        r.Lines.Should().OnlyContain(l => l.Amount == Math.Round(l.Amount, 2));
    }

    // ── Statutory amounts reduce net AND are summed (FR-5e / FR-8) ──────────
    [Fact]
    public void Compute_Statutory_ReducesNetAndIsSummedSeparately()
    {
        var input = new PayrollSlipInput(
            Guid.NewGuid(),
            new[]
            {
                Comp("BASIC", SalaryComponentType.Earning, 40_000m, 1),
                Comp("EPF", SalaryComponentType.Statutory, 3_200m, 2, statutory: true),
                Comp("ETF", SalaryComponentType.Statutory, 1_200m, 3, statutory: true),
            },
            WorkingDays: 21m, LopDays: 0m, ProRataPaidDays: null);

        var r = PayrollSlipCalculator.Compute(input, LopId);

        r.GrossEarnings.Should().Be(40_000m);
        r.TotalDeductions.Should().Be(4_400m);   // 3200 + 1200
        r.NetSalary.Should().Be(35_600m);
        r.StatutoryTotal.Should().Be(4_400m);
    }

    // ── Zero working days never divides by zero (defensive) ─────────────────
    [Fact]
    public void Compute_ZeroWorkingDays_DoesNotThrowAndPaysZero()
    {
        var input = new PayrollSlipInput(
            Guid.NewGuid(),
            new[] { Comp("BASIC", SalaryComponentType.Earning, 10_000m, 1) },
            WorkingDays: 0m, LopDays: 0m, ProRataPaidDays: null);

        var r = PayrollSlipCalculator.Compute(input, LopId);

        r.GrossEarnings.Should().Be(0m);
        r.NetSalary.Should().Be(0m);
    }
}
