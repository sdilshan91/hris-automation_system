// ============================================================================
// ISSUE-335 / G13 — the seed-drift guard, against the REAL seeded rows.
//
// WHY THIS FILE EXISTS AT ALL. The original guard
// (PlanModulesEntitlementTests.SeedVocabulary_IsExactlyTheCanonicalModuleSet_ADM012) asserted
// `PlanModules.All` was equivalent to `PlanModules.All` via a local variable. It never read
// DbInitializer, so the exact regression it advertised — repointing the seed at
// PermissionCatalog.ByModule.Keys — would have left it GREEN. It was documentation wearing a
// [Fact] attribute.
//
// This suite reads what the seeder actually WROTE. It runs on the shared HttpApi harness, which
// boots the genuine ASP.NET Core host in Development against a throwaway Postgres container — so
// DbInitializer.RunAsync has really migrated and really seeded both the `platform` tenant
// (SeedAsync) and the DEV-only `e2e` tenant (SeedE2EDevTenantAsync) before these arms read
// tenants.enabled_modules back out of the database. No production seam is stubbed and no
// vocabulary is re-declared here: the subject is the persisted column.
//
// Mutation-checked (G13, 2026-09-02): repointing the seed at PermissionCatalog.ByModule.Keys turns
// every arm below RED with the exact drift named — unrecognized keys ("Audit"/"CustomField"/…)
// present, canonical CoreHR/Asset/CustomReportBuilder/PublicCareersPage absent. Reverted after.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Authorization;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// US-ADM-012 (ISSUE-335) — proves the tenants DbInitializer seeds carry the CANONICAL
/// <see cref="PlanModules"/> vocabulary in <c>enabled_modules</c>, not the permission-prefix list
/// (<c>PermissionCatalog.ByModule.Keys</c>) it once carried. A module gate reading that legacy data would
/// have denied CoreHR — employees, departments, the dashboard — for every seeded tenant.
/// </summary>
[Collection("HttpApi")]
[Trait("TC", "TC-ADM-012")]
public sealed class PlanModulesSeedDriftApiTests
{
    private readonly ApiTestFactory _factory;

    public PlanModulesSeedDriftApiTests(ApiTestFactory factory) => _factory = factory;

    // The subdomains DbInitializer seeds (DbInitializer.DefaultTenantSubdomain / E2ETenantSubdomain).
    // Deliberately literal rather than referencing the consts: these are the tenants' externally
    // observable identities, so a rename should surface here rather than silently follow along.
    private const string PlatformTenantSubdomain = "platform";
    private const string E2ETenantSubdomain = "e2e";

    /// <summary>
    /// Permission PREFIXES from <c>PermissionCatalog.ByModule.Keys</c> that are NOT module keys. These are the
    /// literal values ISSUE-335 found in <c>enabled_modules</c> on the live dev DB. Note "Reports": the canonical
    /// module key is "Reporting", so the legacy value is also a misspelling of a real key.
    /// </summary>
    private static readonly string[] PermissionPrefixesThatAreNotModuleKeys =
        ["Audit", "CustomField", "Roles", "Tenant", "Reports", "Monitoring", "Impersonation", "EmployeeDocument"];

    // ── The platform admin tenant (DbInitializer.SeedAsync) ──────────────────

    [Fact]
    public async Task PlatformTenant_IsSeededWithTheCanonicalModuleVocabulary_ADM012()
    {
        var seeded = await SeededModulesAsync(PlatformTenantSubdomain);

        AssertCanonicalSeedVocabulary(seeded, PlatformTenantSubdomain);
    }

    // ── The DEV/E2E business tenant (DbInitializer.SeedE2EDevTenantAsync) ────
    // A SECOND seed site writes enabled_modules. Asserting only the platform tenant would leave that one
    // free to drift — the same class of hole this whole item is about.

    [Fact]
    public async Task E2EDevTenant_IsSeededWithTheCanonicalModuleVocabulary_ADM012()
    {
        var seeded = await SeededModulesAsync(E2ETenantSubdomain);

        AssertCanonicalSeedVocabulary(seeded, E2ETenantSubdomain);
    }

    // ── The consequence arm: the gate must GRANT because the data says so, not because it failed open ──

    [Theory]
    [InlineData(PlatformTenantSubdomain)]
    [InlineData(E2ETenantSubdomain)]
    public async Task SeededTenant_IsEntitledToCoreHr_ByData_NotByFailOpen_ADM012(string subdomain)
    {
        var seeded = await SeededModulesAsync(subdomain);

        // IsModuleEnabled alone cannot discriminate here: on the legacy vocabulary it also returns true —
        // that is the deliberate fail-open backstop. So this arm asserts the REASON as well: every token is a
        // recognized key (so the list is authoritative rather than "unparseable"), AND CoreHR is in it.
        seeded.Should().OnlyContain(m => PlanModules.IsValid(m),
            $"'{subdomain}' must be seeded with an authoritative list; one unknown token makes the entitlement "
            + "gate fail OPEN, so a green IsModuleEnabled would prove nothing");
        seeded.Should().Contain(PlanModules.CoreHr);
        PlanModules.IsModuleEnabled(seeded, PlanModules.CoreHr).Should().BeTrue();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AssertCanonicalSeedVocabulary(List<string> seeded, string subdomain)
    {
        seeded.Should().NotBeEmpty(
            $"DbInitializer must seed '{subdomain}' with a module list; an empty column reads as 'unrestricted' "
            + "and would hide a broken seed behind the fail-open rule");

        seeded.Should().OnlyContain(m => PlanModules.IsValid(m),
            $"every module key seeded onto '{subdomain}' must be a canonical PlanModules key");

        seeded.Should().NotIntersectWith(PermissionPrefixesThatAreNotModuleKeys,
            "these are permission prefixes from PermissionCatalog.ByModule.Keys — their presence in "
            + "enabled_modules IS the ISSUE-335 regression");

        // The canonical keys the legacy permission vocabulary has no equivalent for. Named explicitly so a
        // partial drift (a list that is valid but truncated) cannot pass.
        seeded.Should().Contain(PlanModules.CoreHr,
            "CoreHR is always-on; a tenant seeded without it is denied employees, departments and the dashboard");
        seeded.Should().Contain([
            PlanModules.Asset, PlanModules.CustomReportBuilder, PlanModules.PublicCareersPage, PlanModules.Reporting,
        ]);

        seeded.Should().BeEquivalentTo(PlanModules.All,
            "the seed hands out the FULL canonical set — not a subset, and not a different vocabulary");
    }

    /// <summary>
    /// Reads <c>tenants.enabled_modules</c> back out of the database the real DbInitializer seeded.
    /// <c>IgnoreQueryFilters</c> because this is a cross-tenant platform read with no ambient tenant.
    /// </summary>
    private async Task<List<string>> SeededModulesAsync(string subdomain)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenant = await db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Subdomain == subdomain);

        tenant.Should().NotBeNull(
            $"DbInitializer must have seeded the '{subdomain}' tenant — without the row there is nothing to "
            + "assert about, and a silently-skipped seed would make every arm vacuously green");

        return tenant!.EnabledModules;
    }
}
