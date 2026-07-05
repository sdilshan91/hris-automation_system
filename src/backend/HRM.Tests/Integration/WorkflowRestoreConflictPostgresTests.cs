// ============================================================================
// BUG-006 (MED, Admin Console, US-ADM-007) regression — TC-ADM-007-09 (step 5).
//
// WorkflowService.RestoreAsync auto-archives the currently-active workflow for the
// same entity type (BR-2, one-active-per-type) and reactivates the target row, then
// persists BOTH mutations in a SINGLE SaveChanges. PostgreSQL enforces a partial
// unique index `ix_workflow_definitions_tenant_entitytype_active`
// ON (tenant_id, entity_type, is_active) WHERE is_active = true AND is_deleted = false.
// EF Core does not guarantee the UPDATE-to-false (archive the prior active) is flushed
// before the UPDATE-to-true (restore the target), so the index transiently sees two
// is_active=true rows for the same (tenant, entity_type) and raises 23505 → the restore
// throws DbUpdateException, which surfaces as HTTP 500. The fix orders/splits the writes
// so the conflicting row is archived (flushed) before the restored row is reactivated.
//
// WHY POSTGRES (not InMemory): the EF Core InMemory provider does NOT enforce partial
// unique indexes, so the existing unit test
// `WorkflowServiceTests.Restore_AutoArchivesCurrentActiveForSameType()` PASSES on
// InMemory even pre-fix (false confidence — see BUG-006 "why it slipped through").
// Only the project's Testcontainers/Postgres harness (mirrors
// ShiftNameTrimDuplicatePostgresTests / AuditLogSearchPostgresTests) makes the real
// index fire so the pre-fix 500 is reproducible and the fix is proven.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Workflows.DTOs;
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

public sealed class WorkflowRestoreConflictPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

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

    private AppDbContext CreateContext(ITenantContext tc, ICurrentUser cu) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);

    private WorkflowService BuildService(AppDbContext db, ITenantContext tc, ICurrentUser cu) =>
        new(db, tc, cu, NullLogger<WorkflowService>.Instance);

    private static (ITenantContext tc, ICurrentUser cu) Principals(Guid tenantId)
    {
        var tc = new MutableTenantContext { TenantId = tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.Email.Returns("tenantadmin@acme.test");
        return (tc, cu);
    }

    // A minimal single-step Leave workflow (LineManager approver — no seeded-role dependency).
    private static CreateWorkflowRequest LeaveWorkflow(string name, bool activate = true) => new(
        name, "Leave", activate,
        new[] { new WorkflowStepRequest(1, "LineManager", null, false, 24, null, null, null, false, null) });

    /// <summary>
    /// Confirms the partial unique index that turns the dual-UPDATE into a 500 actually exists
    /// after MigrateAsync — otherwise the pre-fix reproduction would be vacuous.
    /// </summary>
    private static async Task AssertActiveUniqueIndexExistsAsync(AppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM pg_indexes WHERE indexname = 'ix_workflow_definitions_tenant_entitytype_active';";
            var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            count.Should().Be(1, "the partial unique index (tenant_id, entity_type, is_active) must exist "
                + "for the pre-fix restore 500 to be reproducible on Postgres");
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // ── BUG-006: restore into a live same-type conflict must SUCCEED, not 500 ──
    [Fact]
    public async Task RestoreWorkflow_SameEntityTypeActiveExists_Succeeds_BUG006()
    {
        var (tc, cu) = Principals(_tenantId);

        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();
        await AssertActiveUniqueIndexExistsAsync(db);

        var svc = BuildService(db, tc, cu);

        // Workflow A: create active, then archive it (so it is a restorable archived Leave workflow).
        var a = (await svc.CreateAsync(LeaveWorkflow("Leave-A"))).Value!;
        (await svc.ArchiveAsync(a.Id)).IsSuccess.Should().BeTrue();

        // Workflow B: a NEW active Leave workflow → now the single active Leave workflow.
        var b = await svc.CreateAsync(LeaveWorkflow("Leave-B"));
        b.IsSuccess.Should().BeTrue(b.Error);

        // Fresh context, as a real second request would use.
        await using var db2 = CreateContext(tc, cu);

        // Restore A while B is active for the same entity type. Pre-fix: RestoreAsync archives B
        // and reactivates A in one SaveChanges → the partial unique index sees two active Leave
        // rows and raises 23505 → DbUpdateException (HTTP 500). This await THROWS pre-fix → the
        // test errors (red). Post-fix: B is archived first, A reactivated → clean success.
        var restored = await BuildService(db2, tc, cu).RestoreAsync(a.Id);

        restored.IsSuccess.Should().BeTrue(restored.Error);
        restored.Value!.IsActive.Should().BeTrue();
        restored.Value!.Status.Should().Be("Active");

        // BR-2 invariant holds on real Postgres: A active, B archived, exactly one active Leave.
        await using var verify = CreateContext(tc, cu);
        (await verify.WorkflowDefinitions.FirstAsync(w => w.Id == a.Id)).IsActive.Should().BeTrue();
        var bRow = await verify.WorkflowDefinitions.FirstAsync(w => w.Id == b.Value!.Id);
        bRow.IsActive.Should().BeFalse();
        bRow.Status.Should().Be(WorkflowStatus.Archived);
        (await verify.WorkflowDefinitions.CountAsync(
            w => w.EntityType == WorkflowEntityType.Leave && w.IsActive)).Should().Be(1);
    }

    // ── Positive control: restore with NO same-type conflict already worked (200) ──
    // Proves the harness/index isn't simply failing every restore — only the conflict path
    // was broken pre-fix; the conflict-free path is the common case and must remain green.
    [Fact]
    public async Task RestoreWorkflow_NoActiveConflict_Succeeds_Control()
    {
        var (tc, cu) = Principals(_tenantId);

        await using var db = CreateContext(tc, cu);
        await db.Database.MigrateAsync();
        await AssertActiveUniqueIndexExistsAsync(db);

        var svc = BuildService(db, tc, cu);

        var a = (await svc.CreateAsync(LeaveWorkflow("Leave-Solo"))).Value!;
        (await svc.ArchiveAsync(a.Id)).IsSuccess.Should().BeTrue();

        // No other active Leave workflow exists → restore has nothing to auto-archive.
        await using var db2 = CreateContext(tc, cu);
        var restored = await BuildService(db2, tc, cu).RestoreAsync(a.Id);

        restored.IsSuccess.Should().BeTrue(restored.Error);
        restored.Value!.IsActive.Should().BeTrue();

        await using var verify = CreateContext(tc, cu);
        (await verify.WorkflowDefinitions.CountAsync(
            w => w.EntityType == WorkflowEntityType.Leave && w.IsActive)).Should().Be(1);
    }
}
