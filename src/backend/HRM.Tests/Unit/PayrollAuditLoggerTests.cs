// ============================================================================
// US-PAY-012: PayrollAuditLogger — unit tests.
//
// The logger is the structured-audit writer (FR-2/FR-3) every payroll write calls. These tests exercise it
// directly against an InMemory AppDbContext (no MediatR) with fake ICurrentUser/ITenantContext, covering:
//   - FR-2/FR-3: Log() stages an AuditLog with the right Action/ResourceType/ResourceId and serializes
//     before/after to JSON (and EventType is kept in sync with Action so legacy reads still see it).
//   - FR-2: before is null for a create (after only); after is null for a delete (before only).
//   - BR-7: systemActor uses the well-known system actor id (never null/empty), and a missing current user
//     (Guid.Empty) also falls back to the system actor rather than the empty Guid.
//   - BR-5: explicit IP / user-agent pass through to the staged entry for sensitive actions.
//   - The entry is tenant-stamped from ITenantContext and is NOT persisted by Log() (caller's SaveChanges
//     commits it) — LogAndSaveAsync persists it itself.
//
// PROVIDER: InMemory AppDbContext — same rationale as the other Payroll unit/integration tests (the verify
// gate runs `dotnet test` with no PostgreSQL / Docker).
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Unit;

public sealed class PayrollAuditLoggerTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();

    private sealed class FixedTenantContext : ITenantContext
    {
        public Guid TenantId { get; init; }
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
            string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; init; }
        public string Email => "hr@t.com";
        public Guid TenantId { get; init; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
    }

    private AppDbContext Db(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, new FixedTenantContext { TenantId = tenantId });
    }

    private PayrollAuditLogger Logger(AppDbContext db, Guid? actorUserId = null) => new(
        db,
        new FixedTenantContext { TenantId = _tenant },
        new FakeCurrentUser { TenantId = _tenant, UserId = actorUserId ?? _actor },
        NullLogger<PayrollAuditLogger>.Instance);

    // ── FR-2 / FR-3: Log() stages a fully-populated structured entry ──────────────

    [Fact]
    public async Task Log_StagesAuditEntry_WithActionResourceAndSerializedBeforeAfter()
    {
        using var db = Db(_tenant);
        var resourceId = Guid.NewGuid().ToString();
        var before = new { Name = "Basic", DefaultValue = 1000m };
        var after = new { Name = "Basic Pay", DefaultValue = 1200m };

        Logger(db).Log(
            PayrollAuditAction.SalaryComponentUpdated,
            PayrollAuditAction.ResourceType.SalaryComponent,
            resourceId, before: before, after: after);
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.AsNoTracking().SingleAsync();
        entry.Action.Should().Be(PayrollAuditAction.SalaryComponentUpdated);
        entry.ResourceType.Should().Be(PayrollAuditAction.ResourceType.SalaryComponent);
        entry.ResourceId.Should().Be(resourceId);
        entry.TenantId.Should().Be(_tenant);
        entry.UserId.Should().Be(_actor);

        // EventType mirrors Action so the legacy (EventType-based) reads still see structured entries.
        entry.EventType.Should().Be(PayrollAuditAction.SalaryComponentUpdated);

        // before/after are serialized to JSON and round-trip with the changed values intact.
        entry.Before.Should().NotBeNullOrWhiteSpace();
        entry.After.Should().NotBeNullOrWhiteSpace();
        JsonSerializer.Deserialize<JsonElement>(entry.Before!).GetProperty("Name").GetString().Should().Be("Basic");
        JsonSerializer.Deserialize<JsonElement>(entry.After!).GetProperty("Name").GetString().Should().Be("Basic Pay");
        JsonSerializer.Deserialize<JsonElement>(entry.After!).GetProperty("DefaultValue").GetDecimal().Should().Be(1200m);
    }

    [Fact]
    public async Task Log_Create_HasNullBefore_AndNonNullAfter()
    {
        using var db = Db(_tenant);
        Logger(db).Log(
            PayrollAuditAction.SalaryComponentCreated,
            PayrollAuditAction.ResourceType.SalaryComponent,
            Guid.NewGuid().ToString(), before: null, after: new { Name = "HRA" });
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.AsNoTracking().SingleAsync();
        entry.Before.Should().BeNull();
        entry.After.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Log_Delete_HasNonNullBefore_AndNullAfter()
    {
        using var db = Db(_tenant);
        Logger(db).Log(
            PayrollAuditAction.SalaryComponentDeleted,
            PayrollAuditAction.ResourceType.SalaryComponent,
            Guid.NewGuid().ToString(), before: new { Name = "HRA" }, after: null);
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.AsNoTracking().SingleAsync();
        entry.Before.Should().NotBeNullOrWhiteSpace();
        entry.After.Should().BeNull();
    }

    [Fact]
    public async Task Log_PreSerializedJsonString_PassesThroughUnchanged()
    {
        using var db = Db(_tenant);
        const string rawJson = "{\"already\":\"serialized\"}";

        Logger(db).Log(
            PayrollAuditAction.SalaryComponentUpdated,
            PayrollAuditAction.ResourceType.SalaryComponent,
            Guid.NewGuid().ToString(), before: rawJson, after: rawJson);
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.AsNoTracking().SingleAsync();
        entry.Before.Should().Be(rawJson);
        entry.After.Should().Be(rawJson);
    }

    // ── BR-7: system actor never null/empty ───────────────────────────────────────

    [Fact]
    public async Task Log_SystemActorTrue_UsesSystemActorId_NotCurrentUser()
    {
        using var db = Db(_tenant);
        Logger(db).Log(
            PayrollAuditAction.PayslipPdfGenerated,
            PayrollAuditAction.ResourceType.PayrollSlip,
            Guid.NewGuid().ToString(), systemActor: true);
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.AsNoTracking().SingleAsync();
        entry.UserId.Should().Be(PayrollAuditAction.SystemActorUserId);
        entry.UserId.Should().NotBe(_actor);
        entry.UserId.Should().NotBeNull();
    }

    [Fact]
    public async Task Log_NoCurrentUser_FallsBackToSystemActor_NotEmptyGuid()
    {
        using var db = Db(_tenant);
        // current user is the empty Guid (e.g. an unauthenticated/job context) and systemActor is NOT set.
        Logger(db, actorUserId: Guid.Empty).Log(
            PayrollAuditAction.PayrollRunFinalized,
            PayrollAuditAction.ResourceType.PayrollRun,
            Guid.NewGuid().ToString());
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.AsNoTracking().SingleAsync();
        entry.UserId.Should().Be(PayrollAuditAction.SystemActorUserId);
        entry.UserId.Should().NotBe(Guid.Empty);
    }

    // ── BR-5: explicit IP / user-agent pass-through for sensitive actions ──────────

    [Fact]
    public async Task Log_ExplicitIpAndUserAgent_AreStamped()
    {
        using var db = Db(_tenant);
        Logger(db).Log(
            PayrollAuditAction.PayrollRunApproved,
            PayrollAuditAction.ResourceType.PayrollRun,
            Guid.NewGuid().ToString(),
            ipAddress: "203.0.113.7", userAgent: "Mozilla/5.0 (audit-test)");
        await db.SaveChangesAsync();

        var entry = await db.AuditLogs.AsNoTracking().SingleAsync();
        entry.IpAddress.Should().Be("203.0.113.7");
        entry.UserAgent.Should().Be("Mozilla/5.0 (audit-test)");
    }

    // ── Staging vs. persistence ───────────────────────────────────────────────────

    [Fact]
    public async Task Log_DoesNotPersist_UntilCallerSaves()
    {
        using var db = Db(_tenant);
        Logger(db).Log(
            PayrollAuditAction.SalaryComponentCreated,
            PayrollAuditAction.ResourceType.SalaryComponent,
            Guid.NewGuid().ToString(), after: new { Name = "Basic" });

        // The entry is staged on the change tracker but not yet committed.
        db.ChangeTracker.Entries<AuditLog>().Should().HaveCount(1);

        using var freshRead = Db(_tenant);
        (await freshRead.AuditLogs.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task LogAndSaveAsync_PersistsTheEntryItself()
    {
        using (var db = Db(_tenant))
        {
            await Logger(db).LogAndSaveAsync(
                PayrollAuditAction.PayslipEmailSent,
                PayrollAuditAction.ResourceType.PayrollSlip,
                Guid.NewGuid().ToString(), systemActor: true);
        }

        using var freshRead = Db(_tenant);
        (await freshRead.AuditLogs.AsNoTracking().CountAsync()).Should().Be(1);
    }
}
