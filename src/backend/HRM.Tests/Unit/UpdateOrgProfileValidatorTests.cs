// ============================================================================
// UpdateOrgProfileValidator — the SERVER-SIDE org-profile input guard (US-ADM-006 AC-1).
//
// DF-20/ISSUE-044: the new leave-cancellation-window bound (0–90) is enforced HERE, on the server. The
// FE max(90) is bypassable by any direct API call, so this validator is the real enforcement layer — it
// had zero test coverage before. These arms pin the new 0–90 rule at its exact boundaries, plus the two
// adjacent numeric bounds (fiscal-month 1–12, probation 1–1825) so a mutant weakening any of them fails.
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.TenantSettings.Commands;
using HRM.Application.Features.TenantSettings.DTOs;
using HRM.Application.Features.TenantSettings.Validators;

namespace HRM.Tests.Unit;

public sealed class UpdateOrgProfileValidatorTests
{
    private readonly UpdateOrgProfileValidator _validator = new();

    // A valid baseline command; each test mutates one field.
    private static UpdateOrgProfileCommand Cmd(
        int leaveWindow = 0, int fiscalMonth = 1, int probation = 90, string name = "Acme Corp")
        => new(new UpdateOrgProfileRequest(
            Name: name, LegalName: null, RegistrationNumber: null, Address: null, Industry: null,
            CompanySize: null, FiscalYearStartMonth: fiscalMonth, DefaultCountryCode: null,
            ProbationPeriodDays: probation, LeaveCancellationWindowDays: leaveWindow));

    // ── DF-20/ISSUE-044: the leave-cancellation-window 0–90 bound (server-side enforcement). ──

    [Theory]
    [Trait("TC", "TC-LV-203")]
    [InlineData(-1)]   // below the floor
    [InlineData(91)]   // above the rail
    [InlineData(1000)]
    public void LeaveCancellationWindowDays_OutOfRange_IsInvalid(int window)
    {
        _validator.TestValidate(Cmd(leaveWindow: window))
            .ShouldHaveValidationErrorFor(x => x.Request.LeaveCancellationWindowDays)
            .WithErrorMessage("Leave cancellation window must be between 0 and 90 days.");
    }

    [Theory]
    [Trait("TC", "TC-LV-203")]
    [InlineData(0)]    // exact floor — today's default behaviour
    [InlineData(90)]   // exact rail
    [InlineData(45)]
    public void LeaveCancellationWindowDays_InRange_IsValid(int window)
    {
        _validator.TestValidate(Cmd(leaveWindow: window))
            .ShouldNotHaveValidationErrorFor(x => x.Request.LeaveCancellationWindowDays);
    }

    // ── Adjacent numeric bounds (regression guard so a mutant weakening either survives no test). ──

    [Theory]
    [Trait("TC", "TC-LV-203")]
    [InlineData(0)]
    [InlineData(13)]
    public void FiscalYearStartMonth_OutOfRange_IsInvalid(int month)
    {
        _validator.TestValidate(Cmd(fiscalMonth: month))
            .ShouldHaveValidationErrorFor(x => x.Request.FiscalYearStartMonth);
    }

    [Theory]
    [Trait("TC", "TC-LV-203")]
    [InlineData(0)]
    [InlineData(1826)]
    public void ProbationPeriodDays_OutOfRange_IsInvalid(int days)
    {
        _validator.TestValidate(Cmd(probation: days))
            .ShouldHaveValidationErrorFor(x => x.Request.ProbationPeriodDays);
    }

    [Fact]
    [Trait("TC", "TC-LV-203")]
    public void ValidBaseline_HasNoErrors()
    {
        _validator.TestValidate(Cmd()).ShouldNotHaveAnyValidationErrors();
    }
}
