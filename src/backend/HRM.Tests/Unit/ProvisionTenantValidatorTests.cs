// ============================================================================
// US-ADM-001: ProvisionTenantCommand validator unit tests.
// Shape-level checks only (AC-2 reserved list, AC-5 invalid formats, field
// requirements). The DB-dependent rules — subdomain already-taken (AC-2/BR-2)
// and plan-must-be-active — live in TenantProvisioningService and are covered
// by the provisioning service tests, not here.
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.Tenants.Commands;
using HRM.Application.Features.Tenants.Validators;

namespace HRM.Tests.Unit;

public sealed class ProvisionTenantValidatorTests
{
    private readonly ProvisionTenantValidator _validator = new();

    private static ProvisionTenantCommand Cmd(
        string name = "Acme Corp",
        string subdomain = "acme",
        Guid? planId = null,
        string ownerEmail = "owner@acme.com",
        string region = "us-east",
        int? trialDays = null)
        => new(name, subdomain, planId ?? Guid.NewGuid(), ownerEmail, region, trialDays);

    [Fact]
    public void Valid_Command_Passes()
    {
        var result = _validator.TestValidate(Cmd());
        result.ShouldNotHaveAnyValidationErrors();
    }

    // ── AC-2: every reserved subdomain is rejected ──────────────────────────

    [Theory]
    [InlineData("www")]
    [InlineData("api")]
    [InlineData("admin")]
    [InlineData("app")]
    [InlineData("mail")]
    [InlineData("status")]
    [InlineData("docs")]
    [InlineData("help")]
    [InlineData("support")]
    [InlineData("static")]
    [InlineData("cdn")]
    [InlineData("dev")]
    [InlineData("stage")]
    [InlineData("prod")]
    [InlineData("test")]
    [InlineData("qa")]
    public void ReservedSubdomain_IsRejected(string reserved)
    {
        var result = _validator.TestValidate(Cmd(subdomain: reserved));
        result.ShouldHaveValidationErrorFor(x => x.Subdomain);
    }

    [Fact]
    public void ReservedSubdomain_IsRejected_CaseInsensitive()
    {
        var result = _validator.TestValidate(Cmd(subdomain: "ADMIN"));
        result.ShouldHaveValidationErrorFor(x => x.Subdomain);
    }

    // ── AC-5: invalid formats (uppercase, special chars, length) ────────────

    [Theory]
    [InlineData("AB")]                 // too short (< 3) + uppercase
    [InlineData("ab")]                 // too short (< 3)
    [InlineData("Acme")]               // uppercase
    [InlineData("acme_corp")]          // underscore (special char)
    [InlineData("acme corp")]          // space
    [InlineData("acme.corp")]          // dot
    [InlineData("-acme")]              // leading hyphen
    [InlineData("acme-")]              // trailing hyphen
    [InlineData("acme!")]              // special char
    public void InvalidSubdomainFormat_IsRejected(string subdomain)
    {
        var result = _validator.TestValidate(Cmd(subdomain: subdomain));
        result.ShouldHaveValidationErrorFor(x => x.Subdomain);
    }

    [Fact]
    public void Subdomain_TooLong_IsRejected()
    {
        var result = _validator.TestValidate(Cmd(subdomain: new string('a', 51)));
        result.ShouldHaveValidationErrorFor(x => x.Subdomain);
    }

    [Fact]
    public void Subdomain_WithHyphenAndDigits_Passes()
    {
        var result = _validator.TestValidate(Cmd(subdomain: "acme-corp-2"));
        result.ShouldNotHaveValidationErrorFor(x => x.Subdomain);
    }

    // ── Other field rules ───────────────────────────────────────────────────

    [Fact]
    public void EmptyName_IsRejected()
    {
        var result = _validator.TestValidate(Cmd(name: ""));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void NameTooLong_IsRejected()
    {
        var result = _validator.TestValidate(Cmd(name: new string('x', 201)));
        result.ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData("missing@")]          // '@' with no domain part — rejected by AspNetCore-compatible EmailAddress mode
    public void InvalidOwnerEmail_IsRejected(string email)
    {
        var result = _validator.TestValidate(Cmd(ownerEmail: email));
        result.ShouldHaveValidationErrorFor(x => x.OwnerEmail);
    }

    [Fact]
    public void EmptyPlanId_IsRejected()
    {
        var result = _validator.TestValidate(Cmd(planId: Guid.Empty));
        result.ShouldHaveValidationErrorFor(x => x.SubscriptionPlanId);
    }

    [Fact]
    public void NegativeTrialDays_IsRejected()
    {
        var result = _validator.TestValidate(Cmd(trialDays: -1));
        result.ShouldHaveValidationErrorFor(x => x.TrialDays);
    }

    [Fact]
    public void ZeroTrialDays_Passes()
    {
        var result = _validator.TestValidate(Cmd(trialDays: 0));
        result.ShouldNotHaveValidationErrorFor(x => x.TrialDays);
    }

    [Fact]
    public void EmptyRegion_IsRejected()
    {
        var result = _validator.TestValidate(Cmd(region: ""));
        result.ShouldHaveValidationErrorFor(x => x.Region);
    }
}
