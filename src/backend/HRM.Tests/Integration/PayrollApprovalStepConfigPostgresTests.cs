// ============================================================================
// DF-16 / US-PAY-008 AC-4 / FR-2 (BUG-076): the payroll-approval step → role config surface on REAL
// PostgreSQL. The three things this suite exists for are ALL provider contracts the EF InMemory provider
// cannot honour, so an InMemory version would be test theater:
//
//   1. ATOMIC REPLACE ordering. SetApprovalStepConfigAsync does RemoveRange(existing) + Add(new) in ONE
//      SaveChanges. Old steps {1,2} and new steps {1,2,3} overlap on step numbers 1 and 2, so if the batch
//      ever inserted a new (tenant,1) BEFORE deleting the old (tenant,1) it would 23505 on
//      ux_payroll_approval_step_config_tenant_step. InMemory enforces no unique index, so it can never fail
//      this way — only a real Npgsql round-trip proves the replace commits.
//   2. The (tenant_id, step_number) UNIQUE INDEX itself (23505 → DbUpdateException).
//   3. The UserTenant → UserTenantRole role-join in UserHoldsRoleAsync, and the EF global tenant filter on
//      the step-config set, both TRANSLATING to SQL on Npgsql.
//
// Harness copied EXACTLY from AttendanceSettingsCrudPostgresTests (IAsyncLifetime + PostgreSqlBuilder
// "postgres:17-alpine" + MigrateAsync + FixedTenantContext + UseNpgsql/UseSnakeCaseNamingConvention +
// TenantInterceptor/AuditInterceptor + EnableRetryOnFailure). UseSnakeCaseNamingConvention() is NOT optional —
// omitting it makes MigrateAsync throw PendingModelChangesWarning.
//
// ⚠ POSTGRES ENFORCES FKs InMemory IGNORES: every Role/UserTenant/UserTenantRole/PayrollRun/StepConfig row
// carries a tenant_id that FKs tenants, so a Tenant row is seeded FIRST for every tenant used. UserTenant.UserId
// FKs users, so the User is seeded too.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class PayrollApprovalStepConfigPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(Guid.NewGuid());
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
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
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private AppDbContext Db(Guid tenantId)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("hr@acme.test");

        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
                .Options,
            tc);
    }

    private PayrollApprovalService Service(AppDbContext db, Guid tenantId, Guid actingUserId)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(actingUserId);
        cu.Email.Returns("actor@acme.test");
        return new PayrollApprovalService(
            db, tc, cu,
            Substitute.For<IPayrollNotificationService>(),
            Substitute.For<IPayrollAuditLogger>(),
            NullLogger<PayrollApprovalService>.Instance);
    }

    // ── seeding ────────────────────────────────────────────────────────

    /// <summary>Seeds the tenant row (FK target for every tenant_id below). Idempotent within a run.</summary>
    private static async Task SeedTenantAsync(AppDbContext db, Guid tenantId, string subdomain)
    {
        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = subdomain, Name = "T" });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Seeds a tenant role that grants Payroll.Approve (so SetApprovalStepConfigAsync accepts it).</summary>
    private static Guid AddApproverRole(AppDbContext db, Guid tenantId, string name)
    {
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = name });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Payroll.Approve });
        return roleId;
    }

    /// <summary>Seeds a user who holds <paramref name="roleId"/> in the tenant (User + Active UserTenant + UserTenantRole).</summary>
    private static void AddRoleHolder(AppDbContext db, Guid tenantId, Guid userId, Guid roleId, string email)
    {
        db.Users.Add(new User { Id = userId, Email = email, DisplayName = email });
        var utId = Guid.NewGuid();
        db.UserTenants.Add(new UserTenant { Id = utId, UserId = userId, TenantId = tenantId, Status = UserTenantStatus.Active });
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
    }

    private static PayrollRun NewAwaitingRun(Guid tenantId, Guid submittedBy, int step, int totalSteps, int payMonth) => new()
    {
        Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayMonth = payMonth, PayYear = 2026,
        Status = PayrollRunStatus.AwaitingApproval, InitiatedBy = submittedBy, InitiatedAt = DateTime.UtcNow,
        TotalEmployees = 2, ProcessedEmployees = 2, TotalGross = 80_000m, TotalNet = 80_000m,
        SubmittedBy = submittedBy, SubmittedAt = DateTime.UtcNow,
        CurrentApprovalStep = step, TotalApprovalSteps = totalSteps, CurrentWorkflowInstanceId = BaseEntity.NewUuidV7(),
    };

    // ══ Arm 1 — the atomic RemoveRange + re-add REPLACE commits on real PG ══

    /// <summary>
    /// Seed a 2-step config directly, then SetApprovalStepConfigAsync with a NEW 3-step set whose steps 1 and 2
    /// point at DIFFERENT roles. The service does RemoveRange(old {1,2}) + Add(new {1,2,3}) in a single
    /// SaveChanges: on real Npgsql this is only safe if the deletes land before the overlapping inserts (else a
    /// 23505 on ux_payroll_approval_step_config_tenant_step). Afterwards exactly the new three rows exist and
    /// neither of the two original roles survives.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-008-16")]
    public async Task SetApprovalStepConfig_AtomicReplace_OverlappingStepNumbers_CommitsTheNewSet()
    {
        var tenantId = Guid.NewGuid();
        Guid roleA, roleB, roleC, roleD, roleE;

        await using (var seed = Db(tenantId))
        {
            await SeedTenantAsync(seed, tenantId, "atomic");
            roleA = AddApproverRole(seed, tenantId, "RoleA");
            roleB = AddApproverRole(seed, tenantId, "RoleB");
            roleC = AddApproverRole(seed, tenantId, "RoleC");
            roleD = AddApproverRole(seed, tenantId, "RoleD");
            roleE = AddApproverRole(seed, tenantId, "RoleE");
            // The initial 2-step config, added directly.
            seed.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, StepNumber = 1, RoleId = roleA });
            seed.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, StepNumber = 2, RoleId = roleB });
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            var result = await Service(db, tenantId, Guid.NewGuid()).SetApprovalStepConfigAsync(
                new[]
                {
                    new PayrollApprovalStepConfigItem { StepNumber = 1, RoleId = roleC },
                    new PayrollApprovalStepConfigItem { StepNumber = 2, RoleId = roleD },
                    new PayrollApprovalStepConfigItem { StepNumber = 3, RoleId = roleE },
                },
                ipAddress: null, default);

            result.IsSuccess.Should().BeTrue(result.Error);
            result.Value!.Should().HaveCount(3);
        }

        await using (var verify = Db(tenantId))
        {
            var rows = await verify.PayrollApprovalStepConfigs.AsNoTracking()
                .OrderBy(c => c.StepNumber).ToListAsync();

            rows.Should().HaveCount(3, "the RemoveRange+re-add replaces the set — no stale step-1/2 rows linger");
            rows[0].StepNumber.Should().Be(1);
            rows[0].RoleId.Should().Be(roleC);
            rows[1].StepNumber.Should().Be(2);
            rows[1].RoleId.Should().Be(roleD);
            rows[2].StepNumber.Should().Be(3);
            rows[2].RoleId.Should().Be(roleE);
            rows.Select(r => r.RoleId).Should().NotContain(new[] { roleA, roleB },
                "the original step roles must be gone after the atomic replace");
        }
    }

    // ══ Arm 2 — the (tenant_id, step_number) unique index (23505 → DbUpdateException) ══

    /// <summary>
    /// A DIRECT insert of two step-config rows sharing (TenantId, StepNumber) must be rejected by
    /// ux_payroll_approval_step_config_tenant_step. On InMemory this passes silently (no unique index); on real
    /// Npgsql it throws DbUpdateException wrapping the 23505 — the contract the atomic-replace ordering depends on.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-008-16")]
    public async Task DuplicateTenantStepNumber_IsRejectedBy_UniqueIndex()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();

        await using (var seed = Db(tenantId))
        {
            await SeedTenantAsync(seed, tenantId, "dupstep");
            seed.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "R" });
            await seed.SaveChangesAsync();
        }

        await using (var db = Db(tenantId))
        {
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, StepNumber = 1, RoleId = roleId });
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, StepNumber = 1, RoleId = roleId });

            var act = async () => await db.SaveChangesAsync();
            await act.Should().ThrowAsync<DbUpdateException>(
                "two rows with the same (tenant_id, step_number) violate the unique index (23505)");
        }
    }

    // ══ Arm 3a — UserHoldsRoleAsync role-join translates on Npgsql (holder true, non-holder false) ══

    /// <summary>
    /// The step-role gate in ApproveAsync (which delegates to UserHoldsRoleAsync's UserTenant → UserTenantRole
    /// join) must TRANSLATE to SQL on Npgsql. A caller who HOLDS the step-1 role passes the gate and the run
    /// becomes Approved; a caller who does NOT hold it is rejected 403 not_step_approver. Neither caller is the
    /// submitter, so maker-checker is not in play; total steps = 1, so the distinct-approver check is not either —
    /// this arm isolates the role-join.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-008-16")]
    public async Task ApproveStepWithConfiguredRole_HolderApproves_NonHolderRejected_OnNpgsql()
    {
        var tenantId = Guid.NewGuid();
        var holderId = Guid.NewGuid();
        var submitterId = Guid.NewGuid();
        var nonHolderId = Guid.NewGuid();
        Guid stepRoleId;
        Guid holderRunId, nonHolderRunId;

        await using (var seed = Db(tenantId))
        {
            await SeedTenantAsync(seed, tenantId, "rolejoin");
            stepRoleId = AddApproverRole(seed, tenantId, "StepOneApprover");
            AddRoleHolder(seed, tenantId, holderId, stepRoleId, "holder@acme.test");
            // Step 1 requires the configured role.
            seed.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, StepNumber = 1, RoleId = stepRoleId });

            var holderRun = NewAwaitingRun(tenantId, submitterId, step: 1, totalSteps: 1, payMonth: 5);
            var nonHolderRun = NewAwaitingRun(tenantId, submitterId, step: 1, totalSteps: 1, payMonth: 6);
            holderRunId = holderRun.Id;
            nonHolderRunId = nonHolderRun.Id;
            seed.PayrollRuns.AddRange(holderRun, nonHolderRun);
            await seed.SaveChangesAsync();
        }

        // The role HOLDER approves step 1 → the run is Approved (UserHoldsRoleAsync returned true).
        await using (var db = Db(tenantId))
        {
            var result = await Service(db, tenantId, holderId).ApproveAsync(holderRunId, null, "10.0.0.1", default);
            result.IsSuccess.Should().BeTrue(result.Error);
            result.Value!.Status.Should().Be(PayrollRunStatus.Approved.ToString());
        }

        // A NON-holder is rejected at the step-role gate (UserHoldsRoleAsync returned false).
        await using (var db = Db(tenantId))
        {
            var result = await Service(db, tenantId, nonHolderId).ApproveAsync(nonHolderRunId, null, "10.0.0.1", default);
            result.IsFailure.Should().BeTrue("a user who does not hold the step-1 role cannot approve it");
            result.ErrorCode.Should().Be("not_step_approver");
            result.StatusCode.Should().Be(403);
        }

        // And the non-holder's run is untouched (still AwaitingApproval, no leaked approval).
        await using (var verify = Db(tenantId))
        {
            var run = await verify.PayrollRuns.AsNoTracking().FirstAsync(r => r.Id == nonHolderRunId);
            run.Status.Should().Be(PayrollRunStatus.AwaitingApproval);
        }
    }

    // ══ Arm 3b — the EF global tenant filter on the step-config set translates on Npgsql ══

    /// <summary>
    /// Tenant B's step-config must be invisible to a tenant-A GetApprovalStepConfigAsync (BR-8). Both tenants
    /// have a distinct one-step config; A sees only its own row, B only its own — proving the global query
    /// filter on payroll_approval_step_config translates to a tenant_id predicate on Npgsql.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-008-16")]
    public async Task GetApprovalStepConfig_IsTenantIsolated_OnNpgsql()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        Guid roleA, roleB;

        await using (var seedA = Db(tenantA))
        {
            await SeedTenantAsync(seedA, tenantA, "tenant-a");
            roleA = AddApproverRole(seedA, tenantA, "A-Approver");
            seedA.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            { Id = BaseEntity.NewUuidV7(), TenantId = tenantA, StepNumber = 1, RoleId = roleA });
            await seedA.SaveChangesAsync();
        }

        await using (var seedB = Db(tenantB))
        {
            await SeedTenantAsync(seedB, tenantB, "tenant-b");
            roleB = AddApproverRole(seedB, tenantB, "B-Approver");
            seedB.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            { Id = BaseEntity.NewUuidV7(), TenantId = tenantB, StepNumber = 1, RoleId = roleB });
            await seedB.SaveChangesAsync();
        }

        await using (var db = Db(tenantA))
        {
            var a = await Service(db, tenantA, Guid.NewGuid()).GetApprovalStepConfigAsync(default);
            a.IsSuccess.Should().BeTrue();
            a.Value!.Should().ContainSingle("tenant A sees only its own step config");
            a.Value![0].RoleId.Should().Be(roleA);
            a.Value.Select(x => x.RoleId).Should().NotContain(roleB, "tenant B's config must be invisible to tenant A");
        }

        await using (var db = Db(tenantB))
        {
            var b = await Service(db, tenantB, Guid.NewGuid()).GetApprovalStepConfigAsync(default);
            b.IsSuccess.Should().BeTrue();
            b.Value!.Should().ContainSingle("tenant B sees only its own step config");
            b.Value![0].RoleId.Should().Be(roleB);
        }
    }
}
