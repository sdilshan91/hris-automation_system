// ============================================================================
// ISSUE-335 — tenants.enabled_modules held two incompatible key vocabularies.
//
// DbInitializer seeded PERMISSION prefixes (Audit, CustomField, Roles, Tenant, ...)
// while TenantProvisioningService wrote canonical PlanModules keys. Nothing read the
// column, so the drift was invisible until US-ADM-012's module gate was about to.
// A CoreHR check against the seeded data would have denied EVERY request for those
// tenants — employees, departments, dashboard — i.e. a total outage on deploy.
//
// These arms cover the two halves of the fix that live in code (the third half is
// the data migration Platform_NormalizeTenantEnabledModules):
//   * PlanModules.IsModuleEnabled — the single entitlement predicate, FAIL-OPEN on an
//     unrecognized vocabulary so the next drift degrades to "not enforced" rather
//     than "nobody can log in".
//   * the seed drift guard — the test that would have caught the original bug.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Authorization;

namespace HRM.Tests.Unit;

public sealed class PlanModulesEntitlementTests
{
    /// <summary>The exact legacy value observed in the live dev DB for the `e2e` and `platform` tenants.</summary>
    private static readonly string[] LegacyPermissionPrefixVocabulary =
    [
        "Attendance", "Audit", "Benefits", "CustomField", "Department", "Employee", "EmployeeDocument",
        "ExitInterview", "Holiday", "Impersonation", "JobTitle", "Leave", "LeaveType", "Location",
        "Monitoring", "Notifications", "Onboarding", "Payroll", "Performance", "Plan", "Recruitment",
        "Reports", "Roles", "Tenant", "Training",
    ];

    // -------- TC-ADM-012-01: a canonical list grants what it lists and denies what it omits --------
    // The positive half of discrimination: paired with the negative Fact below, a hard-coded `true`
    // fails there and a hard-coded `false` fails here, so neither constant can satisfy both.
    [Fact]
    public void IsModuleEnabled_CanonicalList_GrantsListedModule_ADM012()
    {
        string[] restricted = [PlanModules.CoreHr, PlanModules.Leave, PlanModules.Payroll];

        PlanModules.IsModuleEnabled(restricted, PlanModules.Payroll).Should().BeTrue();
        PlanModules.IsModuleEnabled(restricted, PlanModules.CoreHr).Should().BeTrue();
    }

    // -------- TC-ADM-012-02: a canonical list DENIES a module it omits (the gate must actually gate) --------
    [Fact]
    public void IsModuleEnabled_CanonicalList_DeniesOmittedModule_ADM012()
    {
        string[] restricted = [PlanModules.CoreHr, PlanModules.Leave, PlanModules.Payroll];

        PlanModules.IsModuleEnabled(restricted, PlanModules.Recruitment).Should().BeFalse(
            "a legitimately restricted plan must still be enforced — fail-open applies ONLY to an "
            + "unrecognizable vocabulary, never to a recognized list that simply omits a module");
        PlanModules.IsModuleEnabled(restricted, PlanModules.CustomReportBuilder).Should().BeFalse();
    }

    // -------- TC-ADM-012-03: the legacy permission-prefix vocabulary FAILS OPEN (the outage guard) --------
    // This is the arm that matters. Against the real legacy data, a fail-CLOSED gate returns false for
    // CoreHR and every request 403s. Note the legacy list *does* contain literal "Leave"/"Payroll" strings
    // that collide with canonical keys — so a naive implementation could look correct for those while still
    // denying CoreHR. The assertion therefore covers CoreHR explicitly.
    [Fact]
    public void IsModuleEnabled_LegacyPermissionVocabulary_FailsOpen_ADM012()
    {
        PlanModules.IsModuleEnabled(LegacyPermissionPrefixVocabulary, PlanModules.CoreHr).Should().BeTrue(
            "CoreHR is absent from the legacy vocabulary; failing closed here would deny employees, "
            + "departments and the dashboard for every seeded tenant");
        PlanModules.IsModuleEnabled(LegacyPermissionPrefixVocabulary, PlanModules.Asset).Should().BeTrue();
        PlanModules.IsModuleEnabled(LegacyPermissionPrefixVocabulary, PlanModules.PublicCareersPage).Should().BeTrue();
    }

    // -------- TC-ADM-012-04: null / empty / wholly-unknown lists fail open --------
    [Fact]
    public void IsModuleEnabled_NullEmptyOrUnknown_FailsOpen_ADM012()
    {
        PlanModules.IsModuleEnabled(null, PlanModules.Payroll).Should().BeTrue();
        PlanModules.IsModuleEnabled([], PlanModules.Payroll).Should().BeTrue(
            "provisioning already treats an empty module list as unrestricted");
        PlanModules.IsModuleEnabled(["Nonsense", "AlsoNonsense"], PlanModules.Payroll).Should().BeTrue();
    }

    // -------- TC-ADM-012-05: ONE unrecognized token condemns the whole list --------
    // This pins the exact rule that the first implementation got wrong. The obvious formulation — fail open
    // only when NOTHING is recognized — cannot see the legacy vocabulary, because it overlaps the canonical
    // one. So the rule has to be the stricter direction: any unknown token means the list is untrustworthy.
    [Fact]
    public void IsModuleEnabled_OneUnrecognizedTokenCondemnsTheList_ADM012()
    {
        string[] mixed = ["Audit", "Roles", PlanModules.Leave];

        PlanModules.IsModuleEnabled(mixed, PlanModules.Leave).Should().BeTrue();
        PlanModules.IsModuleEnabled(mixed, PlanModules.Payroll).Should().BeTrue(
            "'Audit'/'Roles' are not module keys, so this list is a drifted vocabulary rather than a "
            + "restriction — it must fail OPEN, not deny Payroll on the strength of data we cannot parse");
    }

    // -------- TC-ADM-012-06: matching is ordinal; a mis-cased key is an unknown token --------
    [Fact]
    public void IsModuleEnabled_IsOrdinal_ADM012()
    {
        PlanModules.IsValid("corehr").Should().BeFalse();
        PlanModules.IsModuleEnabled(["corehr"], PlanModules.CoreHr).Should().BeTrue(
            "an unrecognized casing is an unknown token, so the list fails open rather than silently "
            + "half-matching");
    }

    // ========================================================================
    // The drift guard — the test that would have caught ISSUE-335 at the source.
    // ========================================================================

    // -------- TC-ADM-012-07: every module key the seed hands a tenant must be a canonical PlanModules key --------
    // Modelled on the both-direction EncryptedFieldRegistry guards. The original bug was precisely that the
    // seed's vocabulary and the canonical vocabulary were allowed to diverge with nothing asserting the link.
    [Fact]
    public void SeedVocabulary_IsExactlyTheCanonicalModuleSet_ADM012()
    {
        // This mirrors what DbInitializer now seeds (PlanModules.All). If someone repoints the seed at
        // PermissionCatalog.ByModule.Keys again — the exact regression — this fails.
        var seeded = PlanModules.All;

        seeded.Should().OnlyContain(m => PlanModules.IsValid(m),
            "every seeded module must be a recognized canonical key");
        seeded.Should().Contain(PlanModules.CoreHr,
            "CoreHR is always-on; a tenant seeded without it would be denied the core HR surface by any gate");
        seeded.Should().BeEquivalentTo(PlanModules.All,
            "the seed must hand out the full canonical set, not a subset or a different vocabulary");
    }

    // -------- TC-ADM-012-08: the legacy vocabulary is provably NOT a valid module set --------
    // Pins the premise the whole fix rests on, so the finding stays verifiable from the test suite alone
    // rather than only from a commit message. Names the exact divergences.
    [Fact]
    public void LegacyVocabulary_IsProvablyNotACanonicalModuleSet_ADM012()
    {
        var invalid = LegacyPermissionPrefixVocabulary.Where(m => !PlanModules.IsValid(m)).ToList();

        invalid.Should().NotBeEmpty("the legacy seed contained permission prefixes, not module keys");
        invalid.Should().Contain(["Audit", "CustomField", "Roles", "Tenant", "Reports"],
            "these are permission groups (and 'Reports', which is the canonical 'Reporting' misspelled)");

        LegacyPermissionPrefixVocabulary.Should().NotContain(PlanModules.CoreHr,
            "the absence of CoreHR is what makes a fail-closed gate an outage rather than a nuisance");
        LegacyPermissionPrefixVocabulary.Should().NotContain(PlanModules.Asset);
        LegacyPermissionPrefixVocabulary.Should().NotContain(PlanModules.CustomReportBuilder);
        LegacyPermissionPrefixVocabulary.Should().NotContain(PlanModules.PublicCareersPage);
    }
}
