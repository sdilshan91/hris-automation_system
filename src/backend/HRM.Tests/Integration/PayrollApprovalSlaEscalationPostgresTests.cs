// ============================================================================
// ISSUE-173 / US-PAY-008 FR-3: payroll approval SLA auto-escalation on REAL PostgreSQL.
//
// The escalator (`PayrollApprovalSlaEscalator.EscalateDueStepsAsync`) finds AwaitingApproval runs whose
// SlaDueAt has passed and EscalatedAt is null, and escalates each at-most-once via an `ExecuteUpdateAsync`
// compare-and-swap — a real-SQL operation the EF InMemory provider does not honour, so this belongs on
// Testcontainers Postgres. Escalation is NOTIFY (not reassign): stamp EscalatedAt, write an `Escalated`
// PayrollApprovalHistory row, dispatch `payroll-approval-escalated`. A fixed FakeTimeProvider drives "now".
//
// Harness copied from PayrollApprovalStepConfigPostgresTests. ⚠ Postgres enforces the FKs InMemory ignores:
// a Tenant row is seeded first for every tenant_id; UserTenant.UserId FKs users so the User is seeded too.
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

public sealed class PayrollApprovalSlaEscalationPostgresTests : IAsyncLifetime
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

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private (PayrollApprovalSlaEscalator Escalator, IPayrollNotificationService Notifications) NewEscalator(
        AppDbContext db, Guid tenantId, DateTimeOffset now)
    {
        var tc = new FixedTenantContext { TenantId = tenantId };
        var notif = Substitute.For<IPayrollNotificationService>();
        return (new PayrollApprovalSlaEscalator(db, tc, notif,
            NullLogger<PayrollApprovalSlaEscalator>.Instance, new FixedClock(now)), notif);
    }

    private static async Task SeedTenantAsync(AppDbContext db, Guid tenantId)
    {
        if (!await db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Id == tenantId))
        {
            db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = $"t{tenantId:N}"[..12], Name = "T" });
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Seeds a role with a backup holder (User + Active UserTenant + UserTenantRole) + returns the role id.</summary>
    private static Guid AddBackupRoleWithHolder(AppDbContext db, Guid tenantId, Guid userId)
    {
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Backup Approver" });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Payroll.Approve });
        db.Users.Add(new User { Id = userId, Email = $"{userId:N}@acme.test", DisplayName = "Backup" });
        var utId = Guid.NewGuid();
        db.UserTenants.Add(new UserTenant { Id = utId, UserId = userId, TenantId = tenantId, Status = UserTenantStatus.Active });
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        return roleId;
    }

    private static Guid AddAwaitingRun(AppDbContext db, Guid tenantId, DateTime? slaDueAt, Guid? backupRoleId)
    {
        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = tenantId, PayMonth = 5, PayYear = 2026,
            Status = PayrollRunStatus.AwaitingApproval, InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
            TotalEmployees = 2, ProcessedEmployees = 2, TotalGross = 80_000m, TotalNet = 80_000m,
            SubmittedBy = Guid.NewGuid(), SubmittedAt = DateTime.UtcNow,
            CurrentApprovalStep = 1, TotalApprovalSteps = 1, CurrentWorkflowInstanceId = BaseEntity.NewUuidV7(),
            SlaDueAt = slaDueAt, EscalatedAt = null,
        });
        if (backupRoleId is { } br)
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, StepNumber = 1, RoleId = br, BackupRoleId = br, SlaHours = 24,
            });
        return runId;
    }

    // ── A breached run escalates exactly once: EscalatedAt stamped, Escalated history row, notify dispatched. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task EscalateDueSteps_BreachedRun_EscalatesOnce_WritesHistory_Notifies_Idempotent()
    {
        var tenantId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        Guid runId;
        await using (var seed = Db(tenantId))
        {
            await SeedTenantAsync(seed, tenantId);
            var backupRole = AddBackupRoleWithHolder(seed, tenantId, Guid.NewGuid());
            // SlaDueAt one hour in the PAST relative to `now` → breached.
            runId = AddAwaitingRun(seed, tenantId, now.UtcDateTime.AddHours(-1), backupRole);
            await seed.SaveChangesAsync();
        }

        await using (var run1 = Db(tenantId))
        {
            var (escalator, notif) = NewEscalator(run1, tenantId, now);
            (await escalator.EscalateDueStepsAsync()).Should().Be(1);
            await notif.Received(1).NotifyApprovalEventAsync(tenantId, runId, "payroll-approval-escalated", Arg.Any<CancellationToken>());
        }

        await using (var check = Db(tenantId))
        {
            var run = await check.PayrollRuns.AsNoTracking().FirstAsync(r => r.Id == runId);
            run.EscalatedAt.Should().NotBeNull();
            (await check.PayrollApprovalHistories.AsNoTracking()
                .CountAsync(h => h.PayrollRunId == runId && h.Action == PayrollApprovalAction.Escalated)).Should().Be(1);
        }

        // Idempotent: a second pass sees EscalatedAt != null → escalates nothing, no second history row / notify.
        await using (var run2 = Db(tenantId))
        {
            var (escalator, notif) = NewEscalator(run2, tenantId, now.AddHours(1));
            (await escalator.EscalateDueStepsAsync()).Should().Be(0);
            await notif.DidNotReceive().NotifyApprovalEventAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        await using (var check2 = Db(tenantId))
            (await check2.PayrollApprovalHistories.AsNoTracking()
                .CountAsync(h => h.PayrollRunId == runId && h.Action == PayrollApprovalAction.Escalated)).Should().Be(1);
    }

    // ── A run whose SLA has NOT breached (SlaDueAt in the future) is left alone. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task EscalateDueSteps_NotBreached_LeavesRunAlone()
    {
        var tenantId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        Guid runId;
        await using (var seed = Db(tenantId))
        {
            await SeedTenantAsync(seed, tenantId);
            var backupRole = AddBackupRoleWithHolder(seed, tenantId, Guid.NewGuid());
            runId = AddAwaitingRun(seed, tenantId, now.UtcDateTime.AddHours(6), backupRole); // due in the future
            await seed.SaveChangesAsync();
        }

        await using (var run = Db(tenantId))
        {
            var (escalator, notif) = NewEscalator(run, tenantId, now);
            (await escalator.EscalateDueStepsAsync()).Should().Be(0);
            await notif.DidNotReceive().NotifyApprovalEventAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        await using (var check = Db(tenantId))
            (await check.PayrollRuns.AsNoTracking().FirstAsync(r => r.Id == runId)).EscalatedAt.Should().BeNull();
    }

    // ── Isolation (BUG-003 write-side): an escalator running under tenant A never escalates tenant B's breached run. ──
    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task EscalateDueSteps_IsTenantScoped_ForeignBreachedRunUntouched()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        Guid runB;
        await using (var seedB = Db(tenantB))
        {
            await SeedTenantAsync(seedB, tenantB);
            var backup = AddBackupRoleWithHolder(seedB, tenantB, Guid.NewGuid());
            runB = AddAwaitingRun(seedB, tenantB, now.UtcDateTime.AddHours(-1), backup); // tenant B: breached
            await seedB.SaveChangesAsync();
        }
        await using (var seedA = Db(tenantA)) { await SeedTenantAsync(seedA, tenantA); }

        await using (var runAsA = Db(tenantA))
        {
            var (escalator, notif) = NewEscalator(runAsA, tenantA, now);
            (await escalator.EscalateDueStepsAsync()).Should().Be(0); // A's escalator sees none of B's runs
            await notif.DidNotReceive().NotifyApprovalEventAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        await using (var check = Db(tenantB))
            (await check.PayrollRuns.AsNoTracking().FirstAsync(r => r.Id == runB)).EscalatedAt.Should().BeNull();
    }
}
