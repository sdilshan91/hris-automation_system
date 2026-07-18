// ============================================================================
// US-ADM-001: System Admin provisions new tenant — integration tests.
//
// Exercises TenantProvisioningService over a real AppDbContext (InMemory) in the SYSTEM/admin context
// (no resolved tenant — the service uses IgnoreQueryFilters and stamps TenantId explicitly), covering the
// cross-cutting behaviour a validator unit test cannot:
//   - AC-1/AC-4: a happy-path provision writes Tenant + owner User + UserTenant + UserTenantRole(Tenant Owner)
//                + TenantLifecycleEvent("created") + structured AuditLog("Tenant.Provisioned"), and dispatches
//                the welcome email exactly once.
//   - AC-3:      an owner email that already belongs to a global user is LINKED via a new UserTenant row — no
//                duplicate User is created and the result/email flag OwnerLinked=true.
//   - BR-3:      plan TrialDays>0 ⇒ status Trial + TrialEndsAt set; TrialDays=0 ⇒ status Active + no trial end.
//   - NFR-2/BR-2: a second provision with the same subdomain fails (never duplicates).
//   - plan gate: an inactive plan is rejected.
//   - §7:        default master data (the canonical FR-4 default leave-type set + a default General Shift)
//                is seeded (ISSUE-029: was a partial Annual/Sick/Casual-only inline set).
//
// PROVIDER: InMemory — same rationale as the sibling integration tests (the verify gate runs `dotnet test`
// with no PostgreSQL / Docker). Real-PG behaviour is validated by the `migrations` CI job.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Tenants.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class TenantProvisioningIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _actorUserId = Guid.NewGuid();

    // The provisioning service runs in the system/admin context: no tenant is resolved.
    private sealed class SystemTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public string Subdomain => "admin";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => true;
        public bool IsResolved => false;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private AppDbContext Db()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new SystemTenantContext());
    }

    private (TenantProvisioningService Service, ITenantWelcomeEmailService Email) Service()
    {
        var email = Substitute.For<ITenantWelcomeEmailService>();
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(_actorUserId);
        user.IsAuthenticated.Returns(true);
        user.Email.Returns("sysadmin@platform.test");

        var config = new ConfigurationBuilder().Build(); // empty ⇒ service falls back to the default reserved list

        var service = new TenantProvisioningService(
            Db(), user, email, config, NullLogger<TenantProvisioningService>.Instance);
        return (service, email);
    }

    /// <summary>Seeds an active plan and returns its id. TrialDays controls trial-vs-active behaviour (BR-3).</summary>
    private async Task<Guid> SeedPlanAsync(int trialDays = 14, bool isActive = true, int? maxEmployees = 50)
    {
        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Name = "Professional",
            Code = $"pro-{Guid.NewGuid():N}".Substring(0, 12),
            PriceMonthly = 99m,
            TrialDays = trialDays,
            MaxEmployees = maxEmployees,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
        };
        using var db = Db();
        db.SubscriptionPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan.Id;
    }

    private static ProvisionTenantInput Input(Guid planId, string subdomain = "acme",
        string ownerEmail = "owner@acme.com", int? trialDays = null)
        => new("Acme Corp", subdomain, planId, ownerEmail, "us-east", trialDays);

    // ── AC-1 / AC-4: full happy-path provisioning ───────────────────────────

    [Fact]
    public async Task Provision_NewOwner_CreatesTenantMembershipRoleLifecycleAndAudit()
    {
        var planId = await SeedPlanAsync(trialDays: 14);
        var (service, email) = Service();

        var result = await service.ProvisionAsync(Input(planId));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.OwnerLinked.Should().BeFalse();
        result.Value.Status.Should().Be(nameof(TenantStatus.Trial));

        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Subdomain == "acme");
        tenant.Status.Should().Be(TenantStatus.Trial);
        tenant.TrialEndsAt.Should().NotBeNull();
        tenant.BillingEmail.Should().Be("owner@acme.com"); // BR-4

        var user = await db.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == "owner@acme.com");
        var membership = await db.UserTenants.IgnoreQueryFilters()
            .SingleAsync(ut => ut.TenantId == tenant.Id && ut.UserId == user.Id);
        var ownerRole = await db.Roles.IgnoreQueryFilters()
            .SingleAsync(r => r.TenantId == tenant.Id && r.Name == PermissionCatalog.BuiltInRoles.TenantOwner);
        (await db.UserTenantRoles.IgnoreQueryFilters()
            .AnyAsync(utr => utr.UserTenantId == membership.Id && utr.RoleId == ownerRole.Id))
            .Should().BeTrue();

        (await db.TenantLifecycleEvents.IgnoreQueryFilters()
            .AnyAsync(e => e.TenantId == tenant.Id && e.EventType == "created")).Should().BeTrue();
        (await db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.TenantId == tenant.Id && a.Action == "Tenant.Provisioned")).Should().BeTrue();

        await email.Received(1).SendWelcomeAsync(
            Arg.Is<TenantWelcomeEmailMessage>(m => m.Subdomain == "acme" && !m.OwnerExists && m.IsTrial),
            Arg.Any<CancellationToken>());
    }

    // ── AC-3: existing global user is linked, not duplicated ────────────────

    [Fact]
    public async Task Provision_ExistingGlobalUser_LinksWithoutDuplicate()
    {
        var planId = await SeedPlanAsync();
        await using (var seed = Db())
        {
            seed.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                Email = "owner@acme.com",
                DisplayName = "Existing Owner",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var (service, email) = Service();
        var result = await service.ProvisionAsync(Input(planId));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.OwnerLinked.Should().BeTrue();

        using var db = Db();
        (await db.Users.IgnoreQueryFilters().CountAsync(u => u.Email == "owner@acme.com"))
            .Should().Be(1); // no duplicate user
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Subdomain == "acme");
        (await db.UserTenants.IgnoreQueryFilters().AnyAsync(ut => ut.TenantId == tenant.Id))
            .Should().BeTrue();

        await email.Received(1).SendWelcomeAsync(
            Arg.Is<TenantWelcomeEmailMessage>(m => m.OwnerExists), Arg.Any<CancellationToken>());
    }

    // ── BR-3: trial vs active status from trial days ────────────────────────

    [Fact]
    public async Task Provision_ZeroTrialDays_CreatesActiveTenantWithNoTrialEnd()
    {
        var planId = await SeedPlanAsync(trialDays: 0);
        var (service, _) = Service();

        var result = await service.ProvisionAsync(Input(planId));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Status.Should().Be(nameof(TenantStatus.Active));

        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Subdomain == "acme");
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.TrialEndsAt.Should().BeNull();
    }

    // ── NFR-2 / BR-2: idempotency — duplicate subdomain never duplicates ────

    [Fact]
    public async Task Provision_DuplicateSubdomain_FailsAndDoesNotDuplicate()
    {
        var planId = await SeedPlanAsync();
        var (service1, _) = Service();
        (await service1.ProvisionAsync(Input(planId))).IsSuccess.Should().BeTrue();

        var (service2, email2) = Service();
        var second = await service2.ProvisionAsync(Input(planId)); // same subdomain "acme"

        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("subdomain_taken");

        using var db = Db();
        (await db.Tenants.IgnoreQueryFilters().CountAsync(t => t.Subdomain == "acme")).Should().Be(1);
        await email2.DidNotReceive().SendWelcomeAsync(Arg.Any<TenantWelcomeEmailMessage>(), Arg.Any<CancellationToken>());
    }

    // ── plan gate: inactive plan rejected ───────────────────────────────────

    [Fact]
    public async Task Provision_InactivePlan_IsRejected()
    {
        var planId = await SeedPlanAsync(isActive: false);
        var (service, _) = Service();

        var result = await service.ProvisionAsync(Input(planId));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("plan_invalid");
        using var db = Db();
        (await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Subdomain == "acme")).Should().BeFalse();
    }

    // ── US-ADM-009 (FR-6): tenant inherits the chosen plan's enabled modules ─

    [Fact]
    public async Task Provision_InheritsPlanEnabledModules_ExcludingUnselectedModule()
    {
        // Seed a plan whose EnabledModules deliberately EXCLUDES Recruitment (CoreHR + Leave + Payroll only).
        var planId = Guid.NewGuid();
        await using (var seed = Db())
        {
            seed.SubscriptionPlans.Add(new SubscriptionPlan
            {
                Id = planId,
                Name = "Lean",
                Code = $"lean-{Guid.NewGuid():N}".Substring(0, 12),
                PriceMonthly = 49m,
                TrialDays = 0,
                MaxEmployees = 25,
                IsActive = true,
                EnabledModules = new List<string>
                {
                    HRM.Domain.Authorization.PlanModules.CoreHr,
                    HRM.Domain.Authorization.PlanModules.Leave,
                    HRM.Domain.Authorization.PlanModules.Payroll,
                },
                CreatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();
        }

        var (service, _) = Service();
        var result = await service.ProvisionAsync(Input(planId));
        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Subdomain == "acme");
        tenant.EnabledModules.Should().Contain(HRM.Domain.Authorization.PlanModules.CoreHr);
        tenant.EnabledModules.Should().Contain(HRM.Domain.Authorization.PlanModules.Leave);
        tenant.EnabledModules.Should().Contain(HRM.Domain.Authorization.PlanModules.Payroll);
        tenant.EnabledModules.Should().NotContain(HRM.Domain.Authorization.PlanModules.Recruitment);
        tenant.MaxEmployees.Should().Be(25); // also inherited from the plan
    }

    // ── §7: default master data seeded for the new tenant ───────────────────

    [Fact]
    public async Task Provision_SeedsDefaultLeaveTypesAndShift()
    {
        var planId = await SeedPlanAsync();
        var (service, _) = Service();

        await service.ProvisionAsync(Input(planId));

        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Subdomain == "acme");

        var leaveTypeCodes = await db.LeaveTypes.IgnoreQueryFilters()
            .Where(lt => lt.TenantId == tenant.Id).Select(lt => lt.Code).ToListAsync();
        // ISSUE-029: provisioning seeds the canonical FR-4 default set — was the partial AL/SL/CL only.
        // (A system LOP type may additionally be seeded, hence the superset + count-floor assertions.)
        leaveTypeCodes.Should().Contain(new[] { "AL", "SL", "CL", "MAT", "PAT", "BL", "UL" });
        leaveTypeCodes.Should().HaveCountGreaterThanOrEqualTo(7);

        (await db.Shifts.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenant.Id && s.IsDefault))
            .Should().Be(1);
    }

    // ── ISSUE-029 (US-LV-001 FR-4): provisioning seeds the CANONICAL default leave-type set ─────

    [Fact]
    public async Task ProvisionTenant_SeedsCanonicalDefaults_ISSUE029()
    {
        var planId = await SeedPlanAsync();
        var (service, _) = Service();

        await service.ProvisionAsync(Input(planId));

        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Subdomain == "acme");

        var names = await db.LeaveTypes.IgnoreQueryFilters()
            .Where(lt => lt.TenantId == tenant.Id)
            .Select(lt => lt.Name)
            .ToListAsync();

        // FR-4 canonical default set (US-LV-001; authoritative builder LeaveTypeService.GetDefaultLeaveTypes).
        // Pre-fix, provisioning inline-seeded only Annual/Sick/Casual, so the last four were absent.
        names.Should().Contain(new[]
        {
            "Annual Leave", "Sick Leave", "Casual Leave",
            "Maternity Leave", "Paternity Leave", "Bereavement Leave", "Unpaid Leave",
        });
        // Regression discriminator: the types missing from the partial 3-type set are now present.
        names.Should().Contain("Maternity Leave");
        names.Should().Contain("Unpaid Leave");
        // At least the seven canonical FR-4 defaults (a system LOP type may additionally be seeded).
        names.Should().HaveCountGreaterThanOrEqualTo(7);

        // Real seeded data, not just names: the gender-specific default carries its applicability (BR-4),
        // and the unpaid default keeps a zero entitlement (BR-3).
        var maternity = await db.LeaveTypes.IgnoreQueryFilters()
            .SingleAsync(lt => lt.TenantId == tenant.Id && lt.Name == "Maternity Leave");
        maternity.Gender.Should().Be(LeaveTypeGender.Female);

        var unpaid = await db.LeaveTypes.IgnoreQueryFilters()
            .SingleAsync(lt => lt.TenantId == tenant.Id && lt.Name == "Unpaid Leave");
        unpaid.AnnualEntitlement.Should().Be(0m);
    }

    // ── ISSUE-222 (US-LV-011 FR-1): the system LOP leave type is seeded AT tenant provisioning ──
    // (not lazily on first assign). A brand-new tenant that has never issued an assign-lop/compulsory
    // request must already list "Loss of Pay" (code LOP, SystemCategory=LossOfPay, zero entitlement).
    [Fact]
    public async Task ProvisionTenant_SeedsLopSystemLeaveType_WithoutAnyAssign_ISSUE222()
    {
        var planId = await SeedPlanAsync();
        var (service, _) = Service();

        await service.ProvisionAsync(Input(planId));

        using var db = Db();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Subdomain == "acme");

        // No assign-lop / EnsureLopTypeForTenantAsync was ever called — the LOP type must exist purely
        // from provisioning-time seeding (the canonical default set).
        var lop = await db.LeaveTypes.IgnoreQueryFilters()
            .SingleOrDefaultAsync(lt => lt.TenantId == tenant.Id
                && lt.SystemCategory == LeaveTypeSystemCategory.LossOfPay);

        lop.Should().NotBeNull("provisioning must seed the LOP system type at setup, not lazily on first assign");
        lop!.Code.Should().Be("LOP");
        lop.Name.Should().Be("Loss of Pay");
        lop.AnnualEntitlement.Should().Be(0m);
    }
}
