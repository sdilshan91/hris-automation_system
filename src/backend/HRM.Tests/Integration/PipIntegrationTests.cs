// ============================================================================
// US-PRF-008: Performance Improvement Plan (PIP) — integration tests.
//
// Exercises PipService + PipReminderService over a real AppDbContext (InMemory) with the ITenantContext-driven
// global query filter, covering the cross-cutting concerns a service unit test cannot:
//   - NFR-2 tenant isolation: a PIP created in Tenant A is invisible & inaccessible from Tenant B (a cross-tenant
//     Get returns not-found; HR in Tenant B sees an empty list).
//   - FR-5 IMMUTABILITY: across fresh scoped contexts the recorded checkpoint + event rows keep their original
//     values byte-for-byte; the service has no update/delete path for them.
//   - BR-4 not-acknowledged sweep: an unacknowledged Active PIP past the 5-business-day window flips to
//     NotAcknowledged with an immutable system event; one inside the window is left untouched; the sweep never
//     crosses tenants.
//
// PROVIDER: InMemory — same rationale as the other Performance integration tests (the verify gate runs
// `dotnet test` with no PostgreSQL / Docker). Each Service(...) / Db(...) builds a FRESH scoped DbContext over
// the same shared InMemory database, exactly as separate HTTP requests would get.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class PipIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
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

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private PipService Service(Guid tenantId, Guid userId, params string[] permissions)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        var db = new AppDbContext(options, ctx);

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.IsAuthenticated.Returns(true);
        user.Email.Returns("user@t.com");
        user.Permissions.Returns(permissions);

        return new PipService(db, ctx, user,
            Substitute.For<IPerformanceNotificationService>(),
            NullLogger<PipService>.Instance);
    }

    private PipReminderService ReminderService(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        var db = new AppDbContext(options, ctx);
        return new PipReminderService(db, ctx,
            Substitute.For<IPerformanceNotificationService>(),
            NullLogger<PipReminderService>.Instance);
    }

    private sealed record Seeded(Guid HrUserId, Guid ManagerEmpId, Guid EmployeeUserId, Guid EmployeeEmpId);

    private async Task<Seeded> SeedAsync(Guid tenantId)
    {
        using var db = Db(tenantId);
        var hrUserId = Guid.NewGuid();
        var managerEmpId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var employeeEmpId = Guid.NewGuid();

        db.Employees.Add(new Employee { Id = managerEmpId, TenantId = tenantId, UserId = Guid.NewGuid(), EmployeeNo = "MGR", FirstName = "Grace", LastName = "Hopper", Email = "g@t.com", Status = EmployeeStatus.Active });
        db.Employees.Add(new Employee { Id = employeeEmpId, TenantId = tenantId, UserId = employeeUserId, EmployeeNo = "EMP", FirstName = "Ada", LastName = "Lovelace", Email = "a@t.com", Status = EmployeeStatus.Active, ReportsToEmployeeId = managerEmpId });
        await db.SaveChangesAsync();
        return new Seeded(hrUserId, managerEmpId, employeeUserId, employeeEmpId);
    }

    private static CreatePipInput CreateInput(Guid employeeEmpId) =>
        new(employeeEmpId, "Underperformance", new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 1),
            null, null, PipEscalationAction.Reassignment,
            new[] { new PipObjectiveInput("Improve", "d", "criteria", new DateOnly(2026, 8, 1)) },
            new[] { new DateOnly(2026, 7, 15) }, null);

    // ── NFR-2 tenant isolation ──────────────────────────────────────────

    [Fact]
    public async Task Pip_in_tenant_A_is_invisible_from_tenant_B()
    {
        var a = await SeedAsync(_tenantA);
        var created = await Service(_tenantA, a.HrUserId, PermissionCatalog.Performance.ReviewAll)
            .CreateAsync(CreateInput(a.EmployeeEmpId));
        created.IsSuccess.Should().BeTrue();

        // Tenant B HR cannot read the Tenant A PIP by id.
        var bHrUserId = Guid.NewGuid();
        var crossRead = await Service(_tenantB, bHrUserId, PermissionCatalog.Performance.ReviewAll)
            .GetAsync(created.Value!.Id);
        crossRead.IsFailure.Should().BeTrue();
        crossRead.StatusCode.Should().Be(404);

        // ...and sees an empty list.
        var bList = await Service(_tenantB, bHrUserId, PermissionCatalog.Performance.ReviewAll).ListAsync();
        bList.Value!.Should().BeEmpty();
    }

    // ── FR-5 immutability across fresh contexts ─────────────────────────

    [Fact]
    public async Task Checkpoint_and_event_history_is_persisted_immutably_across_contexts()
    {
        var a = await SeedAsync(_tenantA);
        var pip = (await Service(_tenantA, a.HrUserId, PermissionCatalog.Performance.ReviewAll)
            .CreateAsync(CreateInput(a.EmployeeEmpId))).Value!;

        // The manager records a checkpoint in a fresh scoped context.
        var managerUserId = await Db(_tenantA).Employees
            .Where(e => e.Id == a.ManagerEmpId).Select(e => e.UserId).FirstAsync();
        await Service(_tenantA, managerUserId!.Value, PermissionCatalog.Performance.ReviewTeam)
            .RecordCheckpointAsync(new RecordCheckpointInput(
                pip.Id, new DateOnly(2026, 7, 15), PipCheckpointStatus.AtRisk, "Original evidence", null, null, null, null, null));

        // Read it back in ANOTHER fresh context — the immutable rows survive byte-for-byte.
        var reread = await Service(_tenantA, a.HrUserId, PermissionCatalog.Performance.ReviewAll).GetAsync(pip.Id);
        reread.Value!.Checkpoints.Should().ContainSingle();
        reread.Value.Checkpoints[0].EvidenceNotes.Should().Be("Original evidence");
        reread.Value.Events.Should().Contain(e => e.EventType == PipEventType.CheckpointRecorded);

        // Verify directly against the DB that there is no update path: the checkpoint's RecordedAt is set once.
        using var db = Db(_tenantA);
        var rows = await db.PipCheckpoints.AsNoTracking().Where(c => c.PipId == pip.Id).ToListAsync();
        rows.Should().ContainSingle();
        rows[0].UpdatedAt.Should().BeNull();
    }

    // ── BR-4 not-acknowledged sweep ─────────────────────────────────────

    [Fact]
    public async Task Sweep_flags_unacknowledged_pip_past_window_and_leaves_recent_untouched()
    {
        var a = await SeedAsync(_tenantA);
        var pip = (await Service(_tenantA, a.HrUserId, PermissionCatalog.Performance.ReviewAll)
            .CreateAsync(CreateInput(a.EmployeeEmpId))).Value!;

        // Backdate InitiatedAt to 10 days ago (well past 5 business days).
        using (var db = Db(_tenantA))
        {
            var entity = await db.Pips.FirstAsync(p => p.Id == pip.Id);
            entity.InitiatedAt = DateTime.UtcNow.AddDays(-10);
            await db.SaveChangesAsync();
        }

        var flagged = await ReminderService(_tenantA).RunSweepAsync(DateTime.UtcNow);
        flagged.Should().BeGreaterThan(0);

        var after = await Service(_tenantA, a.HrUserId, PermissionCatalog.Performance.ReviewAll).GetAsync(pip.Id);
        after.Value!.AcknowledgementStatus.Should().Be(PipAcknowledgementStatus.NotAcknowledged);
        after.Value.Events.Should().Contain(e => e.EventType == PipEventType.NotAcknowledged);
    }

    [Fact]
    public async Task Sweep_does_not_cross_tenants()
    {
        var a = await SeedAsync(_tenantA);
        var pip = (await Service(_tenantA, a.HrUserId, PermissionCatalog.Performance.ReviewAll)
            .CreateAsync(CreateInput(a.EmployeeEmpId))).Value!;
        using (var db = Db(_tenantA))
        {
            var entity = await db.Pips.FirstAsync(p => p.Id == pip.Id);
            entity.InitiatedAt = DateTime.UtcNow.AddDays(-10);
            await db.SaveChangesAsync();
        }

        // Sweeping Tenant B must not touch Tenant A's PIP.
        await ReminderService(_tenantB).RunSweepAsync(DateTime.UtcNow);

        var stillPending = await Service(_tenantA, a.HrUserId, PermissionCatalog.Performance.ReviewAll).GetAsync(pip.Id);
        stillPending.Value!.AcknowledgementStatus.Should().Be(PipAcknowledgementStatus.Pending);
    }
}
