// ============================================================================
// US-PAY-002 / ISSUE-152 — the declared annual CTC must honour the numeric(18,2) money contract:
// a value with more than 2 decimal places is REJECTED (not silently rounded) across every
// salary-assignment entry point (single-assign, preview, bulk). Pure validator unit tests (no DB).
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.Queries;
using HRM.Application.Features.Payroll.Validators;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-PAY-002-06")]
public sealed class SalaryAssignmentCtcScaleValidatorTests
{
    private readonly AssignSalaryStructureValidator _assign = new();
    private readonly PreviewCtcBreakdownValidator _preview = new();
    private readonly BulkAssignSalaryStructureValidator _bulk = new();

    private static AssignSalaryStructureCommand Assign(decimal ctc) => new(
        EmployeeId: Guid.NewGuid(),
        SalaryStructureId: Guid.NewGuid(),
        EffectiveFrom: new DateOnly(2026, 1, 1),
        AnnualCtc: ctc,
        Reason: null,
        Overrides: Array.Empty<SalaryOverrideInput>());

    private static PreviewCtcBreakdownQuery Preview(decimal ctc) => new(
        SalaryStructureId: Guid.NewGuid(),
        AnnualCtc: ctc,
        Overrides: Array.Empty<SalaryOverrideInput>());

    private static BulkAssignSalaryStructureCommand Bulk(decimal ctc) => new(
        SalaryStructureId: Guid.NewGuid(),
        EffectiveFrom: new DateOnly(2026, 1, 1),
        Reason: null,
        Employees: new[] { new BulkAssignEntryInput(Guid.NewGuid(), ctc, Array.Empty<SalaryOverrideInput>()) });

    // ── Reject: > 2 decimal places (ISSUE-152: 50000.999 → error, NOT rounded) ──────

    [Fact]
    public void Assign_ThreeDecimalPlaces_IsRejected()
        => _assign.TestValidate(Assign(50_000.999m))
            .ShouldHaveValidationErrorFor(x => x.AnnualCtc)
            .WithErrorCode("invalid_ctc_scale");

    [Fact]
    public void Preview_ThreeDecimalPlaces_IsRejected()
        => _preview.TestValidate(Preview(600_000.555m))
            .ShouldHaveValidationErrorFor(x => x.AnnualCtc)
            .WithErrorCode("invalid_ctc_scale");

    [Fact]
    public void Bulk_ThreeDecimalPlaces_IsRejected()
        => _bulk.TestValidate(Bulk(50_000.999m))
            .ShouldHaveValidationErrorFor("Employees[0].AnnualCtc")
            .WithErrorCode("invalid_ctc_scale");

    // ── Accept: exactly 2 dp and whole numbers ──────────────────────────────────────

    [Theory]
    [InlineData(50_000)]      // whole number
    [InlineData(50_000.5)]    // 1 dp
    [InlineData(600_000.25)]  // 2 dp
    public void Assign_TwoOrFewerDecimalPlaces_Passes(double ctc)
        => _assign.TestValidate(Assign((decimal)ctc))
            .ShouldNotHaveValidationErrorFor(x => x.AnnualCtc);

    [Fact]
    public void Assign_ExactlyTwoDecimalPlacesLiteral_Passes()
        => _assign.TestValidate(Assign(50_000.50m))
            .ShouldNotHaveValidationErrorFor(x => x.AnnualCtc);

    [Fact]
    public void Preview_TwoDecimalPlaces_Passes()
        => _preview.TestValidate(Preview(600_000.50m))
            .ShouldNotHaveValidationErrorFor(x => x.AnnualCtc);
}
