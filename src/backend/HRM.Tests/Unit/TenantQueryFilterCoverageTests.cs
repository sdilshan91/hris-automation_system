// ============================================================================
// GAP-S2 — the EF global-query-filter COVERAGE GUARD (the structural half of G1).
//
// Why this file exists: the RLS layer and the EF query-filter layer express the SAME tenant-isolation
// contract, but only one of them was guarded. The RLS layer has a reflection-driven coverage test
// (RlsIsolationPostgresTests, "every tenant-scoped entity has a tenant_isolation policy", asserted as exact
// set equality) and shipped with ZERO holes. The EF filter layer had no such guard and drifted to SIX holes
// (GAP-006) — 132 hand-written HasQueryFilter calls in AppDbContext.OnModelCreating and nothing checking the
// list was complete. Same team, same care, same codebase: the difference was the guard, not the diligence.
// That is finding S-2 of the 2026-08-08 gap analysis ("hand-maintained parallel lists drift; guarded ones do
// not"), and this test is the fix for the CLASS rather than for the six instances.
//
// It deliberately MIRRORS the RLS guard's shape so the two layers stay comparable:
//   1. Enumerate the model for every entity carrying a TenantId property — no hand-maintained list, so a new
//      tenant-scoped entity is covered the moment it is added to the model.
//   2. Assert each one has a query filter that actually references TenantId. "Has *some* filter" is too weak:
//      an entity with only a soft-delete filter (!IsDeleted) would pass that and still be readable across
//      tenants. This is the exact trap AppDbContext's combined `!IsDeleted && (… TenantId …)` filters set.
//   3. Assert the one deliberate exclusion — `User`, the GLOBAL identity table — is not tenant-filtered.
//      Mirrors the RLS guard's `policied.Should().NotContain("users")`. Sharing the exclusion makes a
//      divergence between the two layers visible instead of silent.
//
// Runs on the InMemory provider because it needs the MODEL, not a database: query filters are declared in
// OnModelCreating and are provider-agnostic, so this stays a fast unit test with no Postgres dependency —
// unlike the RLS guard, which must query pg_policies. That matters: `Rls:Enabled=false` in Development, so
// in dev and CI the EF filter is the ONLY isolation layer actually running, and its guard must run there too.
// ============================================================================

using System.Linq.Expressions;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class TenantQueryFilterCoverageTests
{
    /// <summary>
    /// The four entities that carry a TenantId and are deliberately NOT tenant-filtered. This list is the
    /// point of the test: an exclusion has to be added here, in the open, with a reason — it cannot be an
    /// omission nobody notices. Adding a name here is a review-worthy act; forgetting a filter is not.
    ///
    /// <para><b>User</b> — the GLOBAL identity table. <c>users.tenant_id</c> exists (a user's "home" tenant)
    /// but a user is reachable from several tenants through <c>user_tenants</c>, so filtering it by the
    /// ambient tenant would break multi-tenant membership. This is the SAME exclusion the RLS coverage guard
    /// makes (RlsIsolationPostgresTests: <c>t != "users"</c>) — the two layers must exclude it together.</para>
    ///
    /// <para><b>PlanLimitOverride · TenantLifecycleEvent · TenantScheduledJob</b> — SYSTEM/cross-tenant
    /// tables, each stating so in its own XML doc, none inheriting <see cref="Domain.Common.BaseEntity"/>.
    /// They are written and read from the system-admin console while NO tenant is resolved, and — unlike
    /// TenantLatencyBucket, whose five readers all call <c>IgnoreQueryFilters</c> — not one of their 23 read
    /// sites does. A filter would evaluate to <c>TenantId == Guid.Empty</c> in the system context and return
    /// zero rows, so adding one would break tenant lifecycle history, termination-job cancellation on Restore
    /// (US-ADM-004 AC-6/FR-6) and plan-limit overrides (US-ADM-009 FR-4/AC-5). Two compensating controls:
    /// the whole surface sits behind <c>/api/v1/system/*</c>, which <c>SystemEndpointHostGuardMiddleware</c>
    /// fails CLOSED on an unresolved context; and all three DO carry a <c>tenant_isolation</c> RLS policy
    /// (verified against a live database — <c>plan_limit_overrides</c>, <c>tenant_lifecycle_events</c>,
    /// <c>tenant_scheduled_jobs</c> are all in <c>pg_policies</c>), so wherever RLS is enabled they are
    /// protected at the DB layer, and the system-admin path works only because it routes to the BYPASSRLS
    /// <c>hrm_owner</c> role. Note what that means for finding S-2: of these six tables, RLS covered
    /// <b>six</b> and the EF filter layer covered <b>zero</b>. The guard is the whole difference.</para>
    ///
    /// <para><b>Correction to GAP-006, recorded here because this test is what settled it:</b> the gap
    /// analysis listed six unfiltered entities and scoped the fix as "add the 6 HasQueryFilter lines". Only
    /// three were real holes (AuditLog, PayrollReportExport, TenantLatencyBucket — each with a doc comment
    /// claiming a filter it did not have). The other three are deliberate and documented; filtering them
    /// would have been a regression.</para>
    /// </summary>
    private static readonly HashSet<string> DeliberatelyUnfiltered =
    [
        nameof(User),
        nameof(PlanLimitOverride),
        nameof(TenantLifecycleEvent),
        nameof(TenantScheduledJob),
    ];

    [Fact]
    public void Every_tenant_scoped_entity_has_a_query_filter_referencing_TenantId()
    {
        using var db = BuildContext();

        var tenantScoped = TenantScopedEntities(db).ToList();

        // Guard the guard: if this ever comes back empty the assertion below would pass vacuously.
        tenantScoped.Should().HaveCountGreaterThan(100,
            "the model has ~138 tenant-scoped entities; an empty or tiny set means this test stopped looking " +
            "at the real model and is no longer protecting anything");

        var unfiltered = tenantScoped
            .Where(et => !HasTenantIdFilter(et))
            .Select(et => et.DisplayName())
            .OrderBy(n => n)
            .ToList();

        // Joined into one string rather than asserted as a collection: FluentAssertions reports only the
        // first offending item of a collection ("found at least one item {…}"), which turns a six-entity
        // drift into a six-round game of whack-a-mole. The failure must name the whole set at once.
        string.Join(", ", unfiltered).Should().BeEmpty(
            "every entity with a TenantId must carry a global query filter that references TenantId — " +
            "without one, a request whose tenant context is unresolved reads EVERY tenant's rows (BUG-297), " +
            "and in Development/CI (Rls:Enabled=false) this filter is the ONLY isolation layer running. " +
            "Add the HasQueryFilter line in AppDbContext.OnModelCreating alongside its siblings; do NOT add " +
            "the entity to DeliberatelyUnfiltered unless it is genuinely global, in which case the RLS " +
            "coverage guard must be given the same exclusion.");
    }

    [Fact]
    public void The_global_identity_table_is_not_tenant_filtered()
    {
        using var db = BuildContext();

        var user = db.Model.FindEntityType(typeof(User));
        user.Should().NotBeNull();

        HasTenantIdFilter(user!).Should().BeFalse(
            "User is the global identity table: a user belongs to many tenants via user_tenants, so a " +
            "TenantId query filter would hide legitimate cross-tenant membership. The RLS guard excludes " +
            "the same table — if this exclusion is ever removed, remove it in both layers together.");
    }

    /// <summary>
    /// Every entity type in the model that carries a <c>TenantId</c> property, minus the deliberate
    /// exclusions. Mirrors the RLS guard's selector (<c>FindProperty("TenantId") is not null</c>) so the two
    /// layers are measured against an identical definition of "tenant-scoped".
    /// </summary>
    private static IEnumerable<IEntityType> TenantScopedEntities(AppDbContext db) =>
        db.Model.GetEntityTypes()
            .Where(et => et.FindProperty("TenantId") is not null)
            .Where(et => !DeliberatelyUnfiltered.Contains(et.ClrType.Name));

    /// <summary>
    /// True when this entity type — or an ancestor it inherits filters from — declares a query filter whose
    /// expression references TenantId. Walks the hierarchy because a filter is declared on the ROOT of a TPH
    /// hierarchy and inherited by its derived types.
    /// </summary>
    private static bool HasTenantIdFilter(IEntityType entityType)
    {
        for (var et = entityType; et is not null; et = et.BaseType)
        {
            if (et.GetDeclaredQueryFilters().Any(ReferencesTenantId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Asserts on the filter's EXPRESSION rather than its mere presence: an entity whose only filter is
    /// <c>!IsDeleted</c> has a filter but no tenant isolation, and several AppDbContext filters legitimately
    /// combine both concerns in one lambda.
    /// </summary>
    private static bool ReferencesTenantId(IQueryFilter filter)
    {
        LambdaExpression? expression = filter.Expression;
        return expression is not null
            && expression.Body.ToString().Contains("TenantId", StringComparison.Ordinal);
    }

    private static AppDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        // A resolved, non-system tenant: the filters close over ITenantContext, and an unresolved context
        // would make each one the `!IsResolved || …` tautology that BUG-297 is about — which still contains
        // the TenantId reference this test looks for, but pinning a resolved context keeps the model in the
        // shape production actually queries under.
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.IsResolved.Returns(true);
        tenantContext.IsSystemContext.Returns(false);
        tenantContext.TenantId.Returns(Guid.NewGuid());

        return new AppDbContext(options, tenantContext);
    }
}
