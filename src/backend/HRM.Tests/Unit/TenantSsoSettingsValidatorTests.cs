// ============================================================================
// US-AUTH-012 — stateless SSO input rules on the tenant-auth-settings validator.
// Covers FR-4 (GUID/domain format), FR-5/BR-6 (enforcement enum), BR-5 (privilege
// ceiling by role name), AC-4/BR-3 (empty allow-list block), and list-size caps.
// The DB-dependent rules (entitlement 403, role-exists, break-glass) live in the
// service and are exercised by TenantSsoSettingsPostgresTests.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Auth.Commands;
using HRM.Application.Features.Auth.DTOs;
using HRM.Application.Features.Auth.Validators;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-AUTH-012")]
public sealed class TenantSsoSettingsValidatorTests
{
    private readonly TenantAuthSettingsValidator _validator = new();

    private static UpdateTenantAuthSettingsCommand Command(TenantAuthSettingsRequest request)
        => new(Guid.NewGuid(), request);

    private static TenantAuthSettingsRequest Base(Action<TenantAuthSettingsRequest>? _ = null)
        => new() { MfaPolicy = "off" };

    // ── FR-4: Entra tenant IDs must be GUIDs ──────────────────────────────────

    [Fact]
    public void InvalidTid_IsRejected()
    {
        var result = _validator.TestValidate(Command(Base() with
        {
            AllowedEntraTenantIds = ["not-a-guid"],
        }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("valid GUID"));
    }

    [Fact]
    public void ValidTid_IsAccepted()
    {
        var result = _validator.TestValidate(Command(Base() with
        {
            AllowedEntraTenantIds = [Guid.NewGuid().ToString()],
        }));

        result.Errors.Should().NotContain(e => e.ErrorMessage.Contains("GUID"));
    }

    // ── FR-4: email domains must be syntactically valid ───────────────────────

    [Theory]
    [InlineData("not a domain")]
    [InlineData("@contoso.com")]
    [InlineData("contoso")]
    [InlineData("http://contoso.com")]
    public void InvalidDomain_IsRejected(string domain)
    {
        var result = _validator.TestValidate(Command(Base() with
        {
            AllowedEmailDomains = [domain],
        }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("valid domain"));
    }

    [Theory]
    [InlineData("contoso.com")]
    [InlineData("sub.contoso.co.uk")]
    public void ValidDomain_IsAccepted(string domain)
    {
        var result = _validator.TestValidate(Command(Base() with
        {
            AllowedEmailDomains = [domain],
        }));

        result.Errors.Should().NotContain(e => e.ErrorMessage.Contains("domain"));
    }

    // ── AC-4/BR-3: enabling SSO with an explicit empty allow-list is blocked ───

    [Fact]
    public void EnableSsoWithEmptyAllowList_IsRejectedWithExactMessage()
    {
        var result = _validator.TestValidate(Command(Base() with
        {
            SsoEnabled = true,
            AllowedEntraTenantIds = [],
            AllowedEmailDomains = [],
        }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage == "Add at least one trusted directory or email domain before enabling SSO.");
    }

    [Fact]
    public void EnableSsoWithOneDomain_IsAccepted()
    {
        var result = _validator.TestValidate(Command(Base() with
        {
            SsoEnabled = true,
            AllowedEmailDomains = ["contoso.com"],
        }));

        result.IsValid.Should().BeTrue();
    }

    // ── FR-5/BR-6: enforcement mode enum ──────────────────────────────────────

    [Fact]
    public void InvalidEnforcementMode_IsRejected()
    {
        var result = _validator.TestValidate(Command(Base() with { EnforcementMode = "mandatory" }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("optional") && e.ErrorMessage.Contains("sso_only"));
    }

    [Theory]
    [InlineData("optional")]
    [InlineData("sso_only")]
    public void ValidEnforcementMode_IsAccepted(string mode)
    {
        var result = _validator.TestValidate(Command(Base() with { EnforcementMode = mode }));

        result.Errors.Should().NotContain(e => e.PropertyName.Contains("EnforcementMode"));
    }

    // ── BR-5: privilege ceiling on the JIT default role (stateless name check) ─

    [Theory]
    [InlineData("Tenant Owner")]
    [InlineData("Tenant Admin")]
    [InlineData("System Admin")]
    public void PrivilegedJitRole_IsRejected(string role)
    {
        var result = _validator.TestValidate(Command(Base() with { JitDefaultRole = role }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("privileged admin or owner role"));
    }

    [Fact]
    public void NonPrivilegedJitRole_PassesTheStatelessCeilingCheck()
    {
        var result = _validator.TestValidate(Command(Base() with { JitDefaultRole = "Employee" }));

        result.Errors.Should().NotContain(e => e.ErrorMessage.Contains("privileged"));
    }

    // ── list-size cap ─────────────────────────────────────────────────────────

    [Fact]
    public void TooManyDomains_IsRejected()
    {
        var many = Enumerable.Range(0, 101).Select(i => $"d{i}.com").ToList();
        var result = _validator.TestValidate(Command(Base() with { AllowedEmailDomains = many }));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("At most"));
    }

    [Fact]
    public void TwentyEntriesEach_IsAccepted()
    {
        // NFR-4: at least 20 entries each must be supported.
        var tids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid().ToString()).ToList();
        var domains = Enumerable.Range(0, 20).Select(i => $"dir{i}.example.com").ToList();

        var result = _validator.TestValidate(Command(Base() with
        {
            SsoEnabled = true,
            AllowedEntraTenantIds = tids,
            AllowedEmailDomains = domains,
        }));

        result.IsValid.Should().BeTrue();
    }
}
