// ============================================================================
// BUG-307 — a tenant whose plan_id matches no plan must NOT be treated as unlimited.
//
// THE BUG. Ten call sites each hand-wrote the same lookup:
//     .Where(p => p.Code == tenant.PlanId).Select(p => (long?)p.SomeLimit).FirstOrDefaultAsync(ct)
// FirstOrDefaultAsync returns null for TWO different situations — "no plan row" and "plan row whose limit
// is NULL" — and both then flowed on as "unlimited". They are not the same thing: the `enterprise` plan
// genuinely ships max_employees = NULL, so NULL is a legitimate unlimited. That ambiguity is the bug, and
// it is why no call site could tell a deliberate unlimited from a broken plan_id.
//
// Measured on the running database before writing a line of this: 2 of 3 tenants carried
// plan_id = 'default', matching no plan, with a NULL max_employees snapshot as well — so every paid cap
// silently resolved to unlimited. No error, no log, just no limit.
//
// These arms pin the DISTINCTION itself, because that is what all ten call sites will depend on.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;
using Microsoft.Extensions.Logging.Abstractions;
using HRM.Application.Common.Interfaces;

namespace HRM.Tests.Integration;

public sealed class PlanLimitLookupPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _cappedTenantId = Guid.NewGuid();     // starter, max_employees = 25
    private readonly Guid _unlimitedTenantId = Guid.NewGuid();  // enterprise, max_employees = NULL (by design)
    private readonly Guid _brokenTenantId = Guid.NewGuid();     // plan_id = 'default' -> matches no plan

    private readonly MutableTenantContext _tc = new();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tc.SetTenant(_cappedTenantId, "acme", TenantStatus.Active);

        await using var db = Db();
        await db.Database.EnsureCreatedAsync();

        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = BaseEntity.NewUuidV7(), Code = "starter", Name = "Starter", MaxEmployees = 25,
        });
        // NULL max_employees is a DELIBERATE "unlimited" — this row is what makes the ambiguity real.
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = BaseEntity.NewUuidV7(), Code = "enterprise", Name = "Enterprise", MaxEmployees = null,
        });

        db.Tenants.Add(new Tenant { Id = _cappedTenantId, Subdomain = "capped", Name = "Capped", PlanId = "starter" });
        db.Tenants.Add(new Tenant { Id = _unlimitedTenantId, Subdomain = "unltd", Name = "Unltd", PlanId = "enterprise" });
        db.Tenants.Add(new Tenant { Id = _brokenTenantId, Subdomain = "broken", Name = "Broken", PlanId = "default" });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext Db()
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(_tc), new AuditInterceptor(user))
            .Options, _tc);
    }

    private async Task<PlanLimitLookup.EffectivePlanLimit> ResolveFor(Guid tenantId)
    {
        await using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == tenantId);
        return await PlanLimitLookup.ResolveAsync(
            db, tenant, PlanLimitKeys.MaxEmployees, p => p.MaxEmployees, DateTime.UtcNow);
    }

    /// <summary>
    /// THE ARM THAT MATTERS. An unresolvable plan_id is a CONFIGURATION ERROR, and must be reported as one —
    /// not as an unlimited allowance. Before BUG-307 this case was indistinguishable from the arm below.
    /// </summary>
    [Fact]
    public async Task PlanIdMatchingNoPlan_IsAConfigurationError_NotUnlimited_BUG307()
    {
        var limit = await ResolveFor(_brokenTenantId);

        limit.PlanExists.Should().BeFalse("plan_id 'default' matches no subscription_plans row");
        limit.IsConfigurationError.Should().BeTrue(
            "a paid cap that silently resolves to unlimited is a revenue rule failing OPEN, invisibly — "
            + "callers must refuse rather than permit");
        limit.IsUnlimited.Should().BeFalse(
            "this is the whole point: a broken plan_id must NOT masquerade as a deliberate unlimited");
    }

    /// <summary>
    /// THE OTHER HALF. A plan that exists and says NULL is genuinely unlimited — `enterprise` ships exactly
    /// that. If the fix reported this as an error it would deny every enterprise tenant.
    /// </summary>
    [Fact]
    public async Task PlanFoundWithNullLimit_IsGenuinelyUnlimited_BUG307()
    {
        var limit = await ResolveFor(_unlimitedTenantId);

        limit.PlanExists.Should().BeTrue();
        limit.IsConfigurationError.Should().BeFalse(
            "enterprise deliberately has no employee cap — treating that as a misconfiguration would deny "
            + "every enterprise tenant, which is the opposite failure");
        limit.IsUnlimited.Should().BeTrue();
        limit.Value.Should().BeNull();
    }

    /// <summary>
    /// The ordinary case still resolves to a real number, so the guard has not broken normal enforcement.
    /// </summary>
    [Fact]
    public async Task PlanFoundWithACap_ResolvesToThatCap_BUG307()
    {
        var limit = await ResolveFor(_cappedTenantId);

        limit.PlanExists.Should().BeTrue();
        limit.IsConfigurationError.Should().BeFalse();
        limit.IsUnlimited.Should().BeFalse();
        limit.Value.Should().Be(25);
    }

    /// <summary>
    /// An explicit per-tenant override is a deliberate decision, so it remains a valid answer even when the
    /// plan itself does not resolve — otherwise fixing the fail-open would break tenants who PURCHASED an
    /// override while sitting on a mis-set plan_id.
    /// </summary>
    [Fact]
    public async Task AnOverrideStillWins_EvenWhenThePlanDoesNotResolve_BUG307()
    {
        await using (var db = Db())
        {
            db.PlanLimitOverrides.Add(new PlanLimitOverride
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _brokenTenantId,
                LimitKey = PlanLimitKeys.MaxEmployees,
                Value = 99,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var limit = await ResolveFor(_brokenTenantId);

        limit.PlanExists.Should().BeFalse("the plan_id is still unresolvable");
        limit.Source.Should().Be(PlanLimitResolver.LimitSource.Override);
        limit.IsConfigurationError.Should().BeFalse(
            "an explicit override answers the question regardless of whether the plan row resolves");
        limit.Value.Should().Be(99);
    }

    /// <summary>
    /// The startup reconciler must repoint an unresolvable plan_id — and to a plan that PRESERVES the
    /// tenant's existing uncapped behaviour, not one that silently caps them.
    ///
    /// A migration that runs unattended at startup and quietly imposes a cap on a live tenant would be a
    /// worse bug than the one being fixed. `enterprise` has MaxEmployees = NULL, so the tenant stays
    /// uncapped — but now explicitly and enforceably, instead of by accident.
    /// </summary>
    [Fact]
    public async Task StartupReconciler_RepointsAnUnresolvablePlanId_PreservingUncappedBehaviour_BUG307()
    {
        await using (var db = Db())
        {
            await DbInitializer.EnsureResolvablePlanIdAsync(
                db, _brokenTenantId, NullLogger.Instance, CancellationToken.None);
        }

        await using (var verify = Db())
        {
            var tenant = await verify.Tenants.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(t => t.Id == _brokenTenantId);
            tenant.PlanId.Should().Be("enterprise",
                "the repoint must land on a plan that exists AND leaves the tenant uncapped, because that "
                + "is the behaviour it already had — silently capping a live tenant at startup would be the "
                + "opposite mistake");
        }

        var limit = await ResolveFor(_brokenTenantId);
        limit.IsConfigurationError.Should().BeFalse("the plan now resolves, so the fail-closed backstop never fires");
        limit.IsUnlimited.Should().BeTrue("enterprise genuinely has no employee cap");
    }

    /// <summary>
    /// The reconciler must NOT touch a tenant whose plan already resolves — otherwise every startup would
    /// rewrite good data, and a real customer on `starter` would be silently promoted to unlimited.
    /// </summary>
    [Fact]
    public async Task StartupReconciler_LeavesAResolvablePlanIdAlone_BUG307()
    {
        await using (var db = Db())
        {
            await DbInitializer.EnsureResolvablePlanIdAsync(
                db, _cappedTenantId, NullLogger.Instance, CancellationToken.None);
        }

        await using (var verify = Db())
        {
            var tenant = await verify.Tenants.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(t => t.Id == _cappedTenantId);
            tenant.PlanId.Should().Be("starter",
                "a resolvable plan is correct data; rewriting it would promote a capped paying tenant to "
                + "unlimited on every application start");
        }
    }

    /// <summary>
    /// THE NARROWING ARM. A deployment with NO subscription plans at all is not misconfigured — it simply
    /// is not using plan-based limiting, so there is nothing to enforce and nothing to refuse.
    ///
    /// This distinction was learned by breaking things: the first version of the guard reported
    /// "misconfigured" whenever the plan did not resolve, which denied 83 tests whose fixtures create a
    /// tenant and never seed subscription_plans (only 16 of 181 integration files seed any). It was flagging
    /// deployments that had deliberately configured nothing — a far broader behaviour change than the
    /// fail-open it was fixing.
    /// </summary>
    [Fact]
    public async Task NoPlansConfiguredAtAll_IsNotAConfigurationError_BUG307()
    {
        await using (var db = Db())
        {
            db.SubscriptionPlans.RemoveRange(await db.SubscriptionPlans.ToListAsync());
            await db.SaveChangesAsync();
        }

        var limit = await ResolveFor(_brokenTenantId);

        limit.IsConfigurationError.Should().BeFalse(
            "with no plans configured anywhere, plan-based limiting is simply not in use — refusing here "
            + "would deny every deployment that never opted into plans");
        limit.IsUnlimited.Should().BeTrue("nothing to enforce");

        // Restore, so ordering cannot leak into the sibling arms.
        await using (var db = Db())
        {
            db.SubscriptionPlans.Add(new SubscriptionPlan
            { Id = BaseEntity.NewUuidV7(), Code = "starter", Name = "Starter", MaxEmployees = 25 });
            db.SubscriptionPlans.Add(new SubscriptionPlan
            { Id = BaseEntity.NewUuidV7(), Code = "enterprise", Name = "Enterprise", MaxEmployees = null });
            await db.SaveChangesAsync();
        }
    }

    // ── ISSUE-388: the fallback for call sites with no failure channel ───────

    /// <summary>
    /// THE ARM THAT MATTERS FOR THE FALLBACK. Three call sites return a bare int/long/void, so "fail closed"
    /// cannot mean "return an error" — it means return the strictest value any configured plan actually
    /// defines. `starter` caps employees at 25, `enterprise` is NULL (unlimited), so the answer must be 25.
    ///
    /// A NULL plan limit means UNLIMITED, i.e. the OPPOSITE of restrictive. If the helper treated null as a
    /// candidate minimum it would return "unlimited" as the strictest value and reinstate the fail-open in a
    /// new disguise.
    /// </summary>
    [Fact]
    public async Task StrictestConfigured_IgnoresUnlimitedPlans_AndReturnsTheTightestRealCap_ISSUE388()
    {
        await using var db = Db();

        var strictest = await PlanLimitLookup.StrictestConfiguredAsync(db, p => p.MaxEmployees);

        strictest.Should().Be(25,
            "starter caps at 25 and enterprise is NULL (unlimited); a null limit is the opposite of "
            + "restrictive, so treating it as a candidate minimum would return 'unlimited' as the strictest "
            + "value and reinstate the fail-open in a new disguise");
    }

    /// <summary>
    /// With no plan defining the limit at all there is nothing to fall back TO, so the helper returns null and
    /// the caller keeps its own hard default. Returning 0 here would silently block the feature — for the
    /// email dispatcher, that is a total outbound-mail outage caused by a config typo.
    /// </summary>
    [Fact]
    public async Task StrictestConfigured_ReturnsNull_WhenNoPlanDefinesTheLimit_ISSUE388()
    {
        await using var db = Db();

        // Neither seeded plan defines MaxCustomRoles in this fixture.
        var strictest = await PlanLimitLookup.StrictestConfiguredAsync(db, p => p.MaxCustomRoles);

        strictest.Should().BeNull(
            "with nothing to fall back to, the caller must use its own documented default rather than have "
            + "this helper invent a cap — and it must NOT be 0, which would block the feature outright");
    }
}
