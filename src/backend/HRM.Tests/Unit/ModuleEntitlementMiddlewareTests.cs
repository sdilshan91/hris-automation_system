// ============================================================================
// US-ADM-012 (AC-1): ModuleEntitlementMiddleware — the per-request module
// entitlement gate. A route whose owning product module is not in the tenant's
// plan returns 403 module_not_entitled; every platform / CoreHR / unmapped route
// falls open. These unit arms pin the risky route→module table (incl. the traps:
// salary-grades→Payroll outside /payroll, leave-types→Leave, onboarding/assets→
// Asset ahead of onboarding→Onboarding) and the outage guards (CoreHR + legacy
// permission-vocabulary fail-open) deterministically, without booting the stack.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Api.Middleware;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ModuleEntitlementMiddlewareTests
{
    // The exact legacy PERMISSION-prefix vocabulary that was mistakenly seeded into enabled_modules for a long
    // time (ISSUE-335). It overlaps the canonical set (Leave/Payroll/Attendance…) but also carries tokens that are
    // NOT canonical modules (Audit, CustomField, Department, Employee, Reports≠Reporting, Roles, Tenant) — which is
    // exactly what must trip IsModuleEnabled's fail-open backstop. Using the real value as the fixture.
    private static readonly string[] LegacyVocabulary =
    {
        "Attendance", "Audit", "Benefits", "CustomField", "Department", "Employee",
        "Leave", "Payroll", "Reports", "Roles", "Tenant", "Training",
    };

    private static DefaultHttpContext MakeContext(
        string path,
        IReadOnlyCollection<string>? enabledModules,
        bool isResolved = true,
        bool isSystemContext = false,
        bool registerCurrentUser = true)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.IsResolved.Returns(isResolved);
        tenant.IsSystemContext.Returns(isSystemContext);
        tenant.TenantId.Returns(Guid.NewGuid());
        tenant.EnabledModules.Returns(enabledModules ?? Array.Empty<string>());

        var services = new ServiceCollection();
        services.AddSingleton(tenant);
        // Anonymous-safety arms deliberately DON'T register ICurrentUser: if the middleware ever resolved it
        // (GetRequiredService) the request would throw. Not registering it proves the gate never touches auth.
        if (registerCurrentUser)
            services.AddSingleton(Substitute.For<ICurrentUser>());

        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        ctx.Request.Method = "GET";
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream(); // capture the 403 envelope
        return ctx;
    }

    private static async Task<(bool nextCalled, int status, string? code)> Run(HttpContext ctx)
    {
        var nextCalled = false;
        var mw = new ModuleEntitlementMiddleware(
            _ => { nextCalled = true; return Task.CompletedTask; },
            Substitute.For<ILogger<ModuleEntitlementMiddleware>>());

        await mw.InvokeAsync(ctx);

        string? code = null;
        if (ctx.Response.Body is MemoryStream ms && ms.Length > 0)
        {
            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms.ToArray());
            if (doc.RootElement.TryGetProperty("code", out var c))
                code = c.GetString();
        }

        return (nextCalled, ctx.Response.StatusCode, code);
    }

    // ── denial + entitlement ──────────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-ADM-012")]
    public async Task NonEntitled_module_returns_403_module_not_entitled()
    {
        // Attendance excluded from an otherwise-canonical (authoritative) plan ⇒ denied. Discriminates: next is
        // NOT called and the FE-contract code is emitted.
        var (nextCalled, status, code) = await Run(
            MakeContext("/api/v1/attendance/records", new[] { PlanModules.CoreHr, PlanModules.Leave }));

        nextCalled.Should().BeFalse();
        status.Should().Be(StatusCodes.Status403Forbidden);
        code.Should().Be("module_not_entitled");
    }

    [Fact]
    [Trait("TC", "TC-ADM-012")]
    public async Task Entitled_module_passes()
    {
        // SAME path as the denial arm, only the plan differs (Attendance now present) ⇒ passes. The single-variable
        // difference is what makes this discriminate the gate from any unrelated pass/fail.
        var (nextCalled, status, code) = await Run(
            MakeContext("/api/v1/attendance/records", new[] { PlanModules.CoreHr, PlanModules.Attendance }));

        nextCalled.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
        code.Should().BeNull();
    }

    // ── the route→module traps ─────────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-ADM-012")]
    public async Task SalaryGrades_gated_as_Payroll_though_outside_payroll_prefix()
    {
        // Trap: SalaryGradesController sits at /api/v1/tenant/salary-grades, NOT under /payroll. A prefix table
        // that only knew "/payroll" would let this fall open — so a denial here proves the explicit mapping.
        var (nextCalled, _, code) = await Run(
            MakeContext("/api/v1/tenant/salary-grades", new[] { PlanModules.CoreHr })); // Payroll absent

        nextCalled.Should().BeFalse();
        code.Should().Be("module_not_entitled");
    }

    [Fact]
    [Trait("TC", "TC-ADM-012")]
    public async Task LeaveTypes_under_tenant_prefix_gated_as_Leave()
    {
        // Trap: Leave splits across /leaves AND /tenant/leave-types + /tenant/leave-entitlements.
        var (nextCalled, _, code) = await Run(
            MakeContext("/api/v1/tenant/leave-types", new[] { PlanModules.CoreHr })); // Leave absent

        nextCalled.Should().BeFalse();
        code.Should().Be("module_not_entitled");
    }

    [Fact]
    [Trait("TC", "TC-ADM-012")]
    public async Task OnboardingAssets_is_Asset_not_Onboarding_ordering()
    {
        // Trap: Asset has no controller of its own — it lives under OnboardingAssetsController at
        // /onboarding/assets, INSIDE the /onboarding prefix. With Onboarding enabled but Asset NOT, assets must be
        // denied while sibling onboarding routes pass. Proves the more-specific prefix is ordered first.
        var plan = new[] { PlanModules.CoreHr, PlanModules.Onboarding }; // Onboarding yes, Asset no

        var (assetNext, _, assetCode) = await Run(MakeContext("/api/v1/onboarding/assets/123", plan));
        var (onbNext, _, onbCode) = await Run(MakeContext("/api/v1/onboarding/checklists", plan));

        assetNext.Should().BeFalse("Asset is not entitled");
        assetCode.Should().Be("module_not_entitled");
        onbNext.Should().BeTrue("Onboarding IS entitled and must not be swallowed by the assets rule");
        onbCode.Should().BeNull();
    }

    // ── outage guards (the ones that matter most) ───────────────────────────────

    [Theory]
    [Trait("TC", "TC-ADM-012")]
    [InlineData("/api/v1/tenant/employees")]
    [InlineData("/api/v1/tenant/departments")]
    [InlineData("/api/v1/dashboard")]
    [InlineData("/api/v1/holidays")]
    public async Task CoreHR_routes_are_never_denied(string path)
    {
        // A maximally-restricted (but canonical/authoritative) plan of ONLY CoreHr. CoreHR routes are unmapped ⇒
        // fall open. If any of these 403'd, the tenant would lose employees/departments/dashboard — a total outage.
        var (nextCalled, _, code) = await Run(MakeContext(path, new[] { PlanModules.CoreHr }));

        nextCalled.Should().BeTrue();
        code.Should().BeNull();
    }

    [Theory]
    [Trait("TC", "TC-ADM-012")]
    // ⚠ DISCRIMINATION NOTE — read before editing these cases.
    // Only a path whose module is ABSENT from the legacy list actually exercises the fail-open rule. A case
    // whose module happens to appear in the legacy list passes under a naive `EnabledModules.Contains(module)`
    // too, and a CoreHR path is unmapped so the predicate never even runs. Both are still worth keeping as
    // documentation, but they do NOT guard the outage — mutating the predicate to the naive form leaves them
    // green. Verified by mutation: swapping IsModuleEnabled for `Contains` kills ONLY the absent-module cases.
    // If you add a case here, pick a module missing from LegacyVocabulary or you are adding coverage theatre.
    //
    // Absent from LegacyVocabulary (discriminating): Recruitment, Reporting ("Reports" != "Reporting"),
    // Onboarding, Asset, Performance, PublicCareersPage.
    // Present in LegacyVocabulary (non-discriminating): Attendance, Leave, Payroll, Training, Benefits.
    [InlineData("/api/v1/recruitment/vacancies")]        // Recruitment — ABSENT ⇒ discriminates
    [InlineData("/api/v1/reports/headcount")]            // Reporting — ABSENT (legacy has "Reports") ⇒ discriminates
    [InlineData("/api/v1/onboarding/checklists")]        // Onboarding — ABSENT ⇒ discriminates
    [InlineData("/api/v1/onboarding/assets")]            // Asset — ABSENT ⇒ discriminates (also pins map ordering)
    [InlineData("/api/v1/tenant/performance/goals")]     // Performance — ABSENT ⇒ discriminates
    [InlineData("/api/v1/careers/vacancies")]            // PublicCareersPage — ABSENT, anonymous ⇒ discriminates
    [InlineData("/api/v1/attendance/records")]           // Attendance — present; documentation only
    [InlineData("/api/v1/tenant/employees")]             // CoreHR — unmapped; documentation only
    public async Task Legacy_permission_vocabulary_fails_open_no_lockout(string path)
    {
        // THE outage guard: a tenant still carrying the legacy permission vocabulary must NOT be denied anything.
        // Because that list contains tokens we don't recognize as modules, IsModuleEnabled treats the whole list as
        // untrusted and returns true. A fail-CLOSED gate would 403 Recruitment (and CoreHR) here.
        var (nextCalled, _, code) = await Run(MakeContext(path, LegacyVocabulary));

        nextCalled.Should().BeTrue();
        code.Should().BeNull();
    }

    // ── bypasses ────────────────────────────────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-ADM-012")]
    public async Task SystemContext_bypasses_entirely()
    {
        // System/admin operates every tenant; even a "restricted" module list must not gate it. Discriminates via
        // the same attendance-excluded plan that 403s a normal tenant above.
        var (nextCalled, _, code) = await Run(
            MakeContext("/api/v1/attendance/records", new[] { PlanModules.CoreHr }, isSystemContext: true));

        nextCalled.Should().BeTrue();
        code.Should().BeNull();
    }

    [Fact]
    [Trait("TC", "TC-ADM-012")]
    public async Task UnresolvedTenant_bypasses_entirely()
    {
        var (nextCalled, _, code) = await Run(
            MakeContext("/api/v1/attendance/records", new[] { PlanModules.CoreHr }, isResolved: false));

        nextCalled.Should().BeTrue();
        code.Should().BeNull();
    }

    [Theory]
    [Trait("TC", "TC-ADM-012")]
    [InlineData("/api/v1/tenant/users")]
    [InlineData("/api/v1/tenant/settings")]
    [InlineData("/api/v1/notifications")]
    [InlineData("/api/v1/system/tenants")]
    [InlineData("/swagger/index.html")] // non-/api/ path
    public async Task Platform_and_non_api_paths_are_never_gated(string path)
    {
        // Even under a maximally-restricted plan, the cross-cutting console paths must stay reachable — gating them
        // would lock admins out of their own plan/settings. (These are also unmapped, so this proves both the
        // explicit allow-list and the positive-map default agree.)
        var (nextCalled, _, code) = await Run(MakeContext(path, new[] { PlanModules.CoreHr }));

        nextCalled.Should().BeTrue();
        code.Should().BeNull();
    }

    // ── anonymous safety (no ICurrentUser in DI) ────────────────────────────────

    [Fact]
    [Trait("TC", "TC-ADM-012")]
    public async Task Anonymous_careers_request_denies_without_NRE()
    {
        // CareersController/PortalController are [AllowAnonymous]. With PublicCareersPage NOT entitled the gate must
        // still return 403 — and must do so WITHOUT resolving ICurrentUser (not registered here), or it would throw.
        var ctx = MakeContext(
            "/api/v1/careers/vacancies", new[] { PlanModules.CoreHr }, registerCurrentUser: false);

        // If the gate resolved ICurrentUser it would throw here (not registered) and fail the test — that IS the
        // NRE/anonymous-safety discriminator.
        var (nextCalled, status, code) = await Run(ctx);
        nextCalled.Should().BeFalse();
        status.Should().Be(StatusCodes.Status403Forbidden);
        code.Should().Be("module_not_entitled");
    }

    [Fact]
    [Trait("TC", "TC-ADM-012")]
    public async Task Anonymous_careers_request_passes_when_entitled_without_NRE()
    {
        // The entitled counterpart — still no ICurrentUser in DI, still no throw.
        var ctx = MakeContext(
            "/api/v1/careers/vacancies",
            new[] { PlanModules.CoreHr, PlanModules.PublicCareersPage },
            registerCurrentUser: false);

        var (nextCalled, _, code) = await Run(ctx);
        nextCalled.Should().BeTrue();
        code.Should().BeNull();
    }
}
