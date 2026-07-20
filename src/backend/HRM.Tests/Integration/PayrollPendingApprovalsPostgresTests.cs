// ============================================================================
// DF-52 — US-PAY-008 / DF-14: the payroll pending-approvals queue on REAL Postgres.
//
// PayrollApprovalService.GetPendingApprovalsAsync builds an approver's actionable queue by:
//   - filtering to AwaitingApproval runs under the tenant (EF global query filter),
//   - excluding runs the caller can't approve (step-role gate via the UserTenant→UserTenantRole join
//     UserHoldsRoleAsync, maker-checker, distinct-approver), and
//   - resolving submitter display names via a batched Employee/User lookup.
// The InMemory integration/unit suites (PayrollApprovalServiceTests / PayrollApprovalIntegrationTests)
// prove the EF-level logic, but InMemory does NOT translate the multi-table role JOIN or the global filter
// to SQL — a client-eval regression or a filter that doesn't translate would pass InMemory and fail on
// Npgsql. This suite runs those exact paths against a Testcontainers Postgres 17.
//
// Harness copied from AttendanceSettingsCrudPostgresTests. Drives the REAL service directly (service-layer,
// like the other *PostgresTests). UseSnakeCaseNamingConvention() is NOT optional. Each test uses fresh
// tenant/user GUIDs so the shared (once-migrated) database cannot cross-contaminate between methods.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
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

public sealed class PayrollPendingApprovalsPostgresTests : IAsyncLifetime
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
        cu.Email.Returns("seed@acme.test");

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

    /// <summary>Constructs the real service acting as <paramref name="actingUserId"/> in <paramref name="tenantId"/>.</summary>
    private PayrollApprovalService Service(AppDbContext db, Guid tenantId, Guid actingUserId)
    {
        var cu = Substitute.For<ICurrentUser>();
        cu.UserId.Returns(actingUserId);
        cu.IsAuthenticated.Returns(true);
        cu.Email.Returns("approver@acme.test");
        return new PayrollApprovalService(
            db, new FixedTenantContext { TenantId = tenantId }, cu,
            Substitute.For<IPayrollNotificationService>(), Substitute.For<IPayrollAuditLogger>(),
            NullLogger<PayrollApprovalService>.Instance);
    }

    // ── seeding ────────────────────────────────────────────────────────

    private async Task<Guid> SeedRunAsync(
        Guid tenantId, PayrollRunStatus status, Guid submittedBy,
        int? step = null, int? totalSteps = null, Guid? instanceId = null,
        int payMonth = 5, int payYear = 2026)
    {
        await using var db = Db(tenantId);
        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = tenantId, PayMonth = payMonth, PayYear = payYear,
            Status = status, InitiatedBy = submittedBy, InitiatedAt = DateTime.UtcNow,
            TotalEmployees = 2, ProcessedEmployees = 2, TotalGross = 80_000m, TotalNet = 80_000m,
            SubmittedBy = submittedBy, CurrentApprovalStep = step, TotalApprovalSteps = totalSteps,
            CurrentWorkflowInstanceId = instanceId,
        });
        await db.SaveChangesAsync();
        return runId;
    }

    /// <summary>
    /// Seeds a tenant Role granting Payroll.Approve and assigns it to <paramref name="members"/> via the
    /// UserTenant → UserTenantRole join tables (the exact tables UserHoldsRoleAsync / CountEligibleApprovers
    /// query). These join tables are NOT BaseEntity, so tenant id is set explicitly.
    /// </summary>
    private async Task<Guid> SeedApproverRoleAsync(Guid tenantId, string name, params Guid[] members)
    {
        await using var db = Db(tenantId);
        // Postgres enforces fk_roles_tenants_tenant_id (InMemory does not) — seed the Tenant row.
        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId))
            db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = $"t{tenantId:N}"[..12], Name = "T" });
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = name });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Payroll.Approve });
        foreach (var uid in members)
        {
            // Postgres enforces the users FK (InMemory does not) — seed the User row for each member.
            if (!await db.Users.AnyAsync(u => u.Id == uid))
                db.Users.Add(new User { Id = uid, Email = $"{uid}@acme.test" });
            var utId = Guid.NewGuid();
            db.UserTenants.Add(new UserTenant { Id = utId, UserId = uid, TenantId = tenantId, Status = UserTenantStatus.Active });
            db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        }
        await db.SaveChangesAsync();
        return roleId;
    }

    private async Task SeedStepConfigAsync(Guid tenantId, int step, Guid roleId)
    {
        await using var db = Db(tenantId);
        db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, StepNumber = step, RoleId = roleId,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds an Employee linked to <paramref name="userId"/> (with the REQUIRED Department + JobTitle FKs Postgres enforces).</summary>
    private async Task SeedEmployeeForUserAsync(Guid tenantId, Guid userId, string first, string last)
    {
        await using var db = Db(tenantId);
        var dept = new Department { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, Name = "Fin", Code = "FIN" };
        var title = new JobTitle { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TitleName = "Analyst" };
        db.Departments.Add(dept);
        db.JobTitles.Add(title);
        // Postgres enforces fk_employees_users_user_id (InMemory does not) — seed the linked User row.
        if (!await db.Users.AnyAsync(u => u.Id == userId))
            db.Users.Add(new User { Id = userId, Email = "sub@acme.test" });
        db.Employees.Add(new Employee
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, UserId = userId,
            EmployeeNo = "EMP-7001", FirstName = first, LastName = last, Email = "sub@acme.test",
            DepartmentId = dept.Id, JobTitleId = title.Id,
            Status = EmployeeStatus.Active, IsActive = true, IsDeleted = false,
        });
        await db.SaveChangesAsync();
    }

    // ══ Arm 1 — tenant scoping (BUG-003 class): the global filter translates to SQL on Postgres ══

    /// <summary>
    /// Seed AwaitingApproval runs under tenant A and tenant B. A tenant-A approver's queue returns ONLY the
    /// tenant-A run. With no step config and no seeded approvers, the small-team exception relaxes
    /// maker-checker, so a plain AwaitingApproval run (submitted by someone else) is included — isolating the
    /// tenant filter as the thing under test. The unit test proves the EF filter; this proves it translates
    /// on Npgsql.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_IsTenantScoped_OnPostgres()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var approverA = Guid.NewGuid();

        var runA = await SeedRunAsync(tenantA, PayrollRunStatus.AwaitingApproval, submittedBy: Guid.NewGuid());
        var runB = await SeedRunAsync(tenantB, PayrollRunStatus.AwaitingApproval, submittedBy: Guid.NewGuid());

        await using var db = Db(tenantA);
        var result = await Service(db, tenantA, approverA).GetPendingApprovalsAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        var ids = result.Value!.Select(r => r.RunId).ToList();
        ids.Should().Contain(runA);
        ids.Should().NotContain(runB, "tenant B's run must be invisible to a tenant-A approver");
    }

    // ══ Arm 2 — the step-role JOIN (UserHoldsRoleAsync) translates to SQL on Postgres ══

    /// <summary>
    /// With a step-1 role configured, a run appears in the queue ONLY for a caller who HOLDS that role (the
    /// UserTenant→UserTenantRole join). A holder sees the run; a non-holder is excluded. This exercises the
    /// multi-table join under Npgsql — a client-eval regression would still pass InMemory but throw or
    /// mis-filter here. The submitter is a third party so maker-checker never confounds the result.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_StepRoleJoin_Translates_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        var holder = Guid.NewGuid();
        var nonHolder = Guid.NewGuid();
        var submitter = Guid.NewGuid();

        var roleId = await SeedApproverRoleAsync(tenantId, "StepApprover", holder);
        await SeedStepConfigAsync(tenantId, step: 1, roleId: roleId);
        var runId = await SeedRunAsync(tenantId, PayrollRunStatus.AwaitingApproval,
            submittedBy: submitter, step: 1, totalSteps: 1, instanceId: Guid.NewGuid());

        // The holder (assigned the step role) sees the run.
        await using (var db = Db(tenantId))
        {
            var held = await Service(db, tenantId, holder).GetPendingApprovalsAsync();
            held.IsSuccess.Should().BeTrue(held.Error);
            held.Value!.Select(r => r.RunId).Should().Contain(runId);
        }

        // A caller without the step role is excluded (the join returns no membership).
        await using (var db = Db(tenantId))
        {
            var notHeld = await Service(db, tenantId, nonHolder).GetPendingApprovalsAsync();
            notHeld.IsSuccess.Should().BeTrue(notHeld.Error);
            notHeld.Value!.Select(r => r.RunId).Should().NotContain(runId,
                "a caller who does not hold the configured step role cannot approve, so the run is not queued for them");
        }
    }

    // ══ Arm 3 — batched submitter-name resolution via the linked Employee on Postgres ══

    /// <summary>
    /// A run whose submitter has a linked tenant Employee resolves InitiatedByName to the employee's
    /// "First Last" via the batched keyed lookup (no N+1). Proves that batched-lookup query runs on Npgsql
    /// and joins to the right row. No step config ⇒ the run is queued for the (different) approver.
    /// </summary>
    [Fact]
    [Trait("TC", "TC-PAY-008-14")]
    public async Task GetPending_ResolvesSubmitterName_FromLinkedEmployee_OnPostgres()
    {
        var tenantId = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var submitter = Guid.NewGuid();

        await SeedEmployeeForUserAsync(tenantId, submitter, "Ada", "Lovelace");
        var runId = await SeedRunAsync(tenantId, PayrollRunStatus.AwaitingApproval,
            submittedBy: submitter, step: 1, totalSteps: 1, instanceId: Guid.NewGuid());

        await using var db = Db(tenantId);
        var result = await Service(db, tenantId, approver).GetPendingApprovalsAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        var row = result.Value!.Single(r => r.RunId == runId);
        row.SubmittedBy.Should().Be(submitter);
        row.InitiatedByName.Should().Be("Ada Lovelace");
    }
}
