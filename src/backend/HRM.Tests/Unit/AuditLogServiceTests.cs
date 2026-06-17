// ============================================================================
// US-ADM-008: Tenant-Admin audit-log READ + EXPORT service tests.
// Covers tenant isolation (AC-1), filters + pagination + desc sort (AC-2/FR-1),
// masked detail (AC-3/FR-4), export audit row + masked content (AC-4/BR-4), and
// the retention PURGE (FR-6). EF Core InMemory only (no Testcontainers); all
// timestamps are seeded explicitly (no sleeping).
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.AuditLog;
using HRM.Application.Features.AuditLog.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AuditLogServiceTests
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _actorUser = Guid.NewGuid();
    private readonly Guid _otherActor = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AuditLogService> _logger = Substitute.For<ILogger<AuditLogService>>();
    private readonly ILogger<AuditLogPurgeService> _purgeLogger = Substitute.For<ILogger<AuditLogPurgeService>>();

    public AuditLogServiceTests()
    {
        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_actorUser);
    }

    private static ITenantContext Ctx(Guid tenantId)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    private AppDbContext Db(Guid tenantId) => TestDbContextFactory.Create(Ctx(tenantId), _dbName);

    private AuditLogService Service(Guid tenantId)
        => new(Db(tenantId), Ctx(tenantId), _currentUser, _logger);

    private static readonly DateTime Base = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private async Task SeedTenantsAndUsersAsync()
    {
        using var db = Db(_tenantA);
        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "tenant-a", Name = "Tenant A", AuditLogRetentionDays = 90 });
        db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "tenant-b", Name = "Tenant B", AuditLogRetentionDays = 90 });
        db.Users.Add(new User { Id = _actorUser, Email = "actor@a.com", DisplayName = "Actor A" });
        db.Users.Add(new User { Id = _otherActor, Email = "other@a.com", DisplayName = "Other A" });
        await db.SaveChangesAsync();
    }

    private async Task AddAuditAsync(
        Guid tenantId, DateTime createdAt, Guid? userId = null,
        string action = "Employee.Update", string? resourceType = "Employee", string? resourceId = "emp-1",
        string? before = null, string? after = null, string? detail = null)
    {
        using var db = Db(tenantId);
        db.AuditLogs.Add(new AuditLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            UserId = userId,
            EventType = action,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            Before = before,
            After = after,
            Detail = detail,
            CreatedAt = createdAt,
        });
        await db.SaveChangesAsync();
    }

    // ── AC-1: tenant isolation ──────────────────────────────────────────────

    [Fact]
    public async Task List_OnlyReturnsCurrentTenantRows()
    {
        await SeedTenantsAndUsersAsync();
        await AddAuditAsync(_tenantA, Base, _actorUser, action: "A.Event");
        await AddAuditAsync(_tenantA, Base.AddMinutes(1), _actorUser, action: "A.Event2");
        await AddAuditAsync(_tenantB, Base, _otherActor, action: "B.Event");

        var result = await Service(_tenantA).ListAsync(EmptyFilter(), page: 1, pageSize: 50);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Items.Should().HaveCount(2);
        result.Value.Items.Should().OnlyContain(i => i.Action.StartsWith("A."));
        result.Value.TotalCount.Should().Be(2);
        result.Value.RetentionDays.Should().Be(90);
    }

    // ── AC-2 / FR-1: reverse-chronological + pagination ─────────────────────

    [Fact]
    public async Task List_IsReverseChronologicalAndPaginated()
    {
        await SeedTenantsAndUsersAsync();
        for (var i = 0; i < 5; i++)
            await AddAuditAsync(_tenantA, Base.AddMinutes(i), _actorUser, action: $"Event{i}");

        var page1 = await Service(_tenantA).ListAsync(EmptyFilter(), page: 1, pageSize: 2);
        page1.Value!.Items.Should().HaveCount(2);
        page1.Value.TotalCount.Should().Be(5);
        // Newest first: Event4 then Event3.
        page1.Value.Items[0].Action.Should().Be("Event4");
        page1.Value.Items[1].Action.Should().Be("Event3");

        var page3 = await Service(_tenantA).ListAsync(EmptyFilter(), page: 3, pageSize: 2);
        page3.Value!.Items.Should().HaveCount(1);
        page3.Value.Items[0].Action.Should().Be("Event0");
    }

    [Fact]
    public async Task List_PageSizeIsClampedToMax200()
    {
        await SeedTenantsAndUsersAsync();
        await AddAuditAsync(_tenantA, Base, _actorUser);

        var result = await Service(_tenantA).ListAsync(EmptyFilter(), page: 1, pageSize: 5000);

        result.Value!.PageSize.Should().Be(200);
    }

    // ── AC-2: filters ────────────────────────────────────────────────────────

    [Fact]
    public async Task List_FiltersByActorActionResourceTypeAndDateRange()
    {
        await SeedTenantsAndUsersAsync();
        await AddAuditAsync(_tenantA, Base.AddDays(-10), _actorUser, action: "Leave.Approve", resourceType: "Leave");
        await AddAuditAsync(_tenantA, Base.AddDays(-1), _otherActor, action: "Employee.Update", resourceType: "Employee");
        await AddAuditAsync(_tenantA, Base, _actorUser, action: "Employee.Update", resourceType: "Employee");

        var svc = Service(_tenantA);

        (await svc.ListAsync(EmptyFilter() with { ActorUserId = _actorUser }, 1, 50))
            .Value!.Items.Should().HaveCount(2);

        (await svc.ListAsync(EmptyFilter() with { Action = "Employee.Update" }, 1, 50))
            .Value!.Items.Should().HaveCount(2);

        (await svc.ListAsync(EmptyFilter() with { ResourceType = "Leave" }, 1, 50))
            .Value!.Items.Should().HaveCount(1);

        // Date range excluding the -10d row.
        (await svc.ListAsync(EmptyFilter() with { StartDate = Base.AddDays(-2) }, 1, 50))
            .Value!.Items.Should().HaveCount(2);

        // Combined (AND): actor AND action AND date.
        (await svc.ListAsync(
                EmptyFilter() with { ActorUserId = _actorUser, Action = "Employee.Update", StartDate = Base.AddDays(-2) }, 1, 50))
            .Value!.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task List_SearchMatchesBeforeAfterAndDetail()
    {
        await SeedTenantsAndUsersAsync();
        await AddAuditAsync(_tenantA, Base, _actorUser, after: "{\"name\":\"Wolverine\"}");
        await AddAuditAsync(_tenantA, Base.AddMinutes(1), _actorUser, detail: "User Magneto logged in");
        await AddAuditAsync(_tenantA, Base.AddMinutes(2), _actorUser, after: "{\"name\":\"Cyclops\"}");

        var svc = Service(_tenantA);

        (await svc.ListAsync(EmptyFilter() with { SearchQuery = "Wolverine" }, 1, 50)).Value!.Items.Should().HaveCount(1);
        (await svc.ListAsync(EmptyFilter() with { SearchQuery = "Magneto" }, 1, 50)).Value!.Items.Should().HaveCount(1);
        (await svc.ListAsync(EmptyFilter() with { SearchQuery = "Nightcrawler" }, 1, 50)).Value!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task List_ResolvesActorNameAndEmail()
    {
        await SeedTenantsAndUsersAsync();
        await AddAuditAsync(_tenantA, Base, _actorUser);

        var item = (await Service(_tenantA).ListAsync(EmptyFilter(), 1, 50)).Value!.Items.Single();

        item.ActorName.Should().Be("Actor A");
        item.ActorEmail.Should().Be("actor@a.com");
    }

    // ── AC-3 / FR-4: detail masking ──────────────────────────────────────────

    [Fact]
    public async Task Detail_MasksSensitiveFieldsInBeforeAndAfter()
    {
        await SeedTenantsAndUsersAsync();
        var id = BaseEntity.NewUuidV7();
        using (var db = Db(_tenantA))
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = id,
                TenantId = _tenantA,
                UserId = _actorUser,
                EventType = "Employee.Update",
                Action = "Employee.Update",
                ResourceType = "Employee",
                ResourceId = "emp-1",
                Before = "{\"bank_account_number\":\"111222333\",\"national_id\":\"NID-9\"}",
                After = "{\"bank_account_number\":\"999888777\",\"password_hash\":\"hashed\"}",
                IpAddress = "10.0.0.1",
                UserAgent = "Mozilla",
                TraceId = "trace-1",
                CreatedAt = Base,
            });
            await db.SaveChangesAsync();
        }

        var result = await Service(_tenantA).GetAsync(id);

        result.IsSuccess.Should().BeTrue(result.Error);
        var dto = result.Value!;
        dto.Before.Should().Contain(SensitiveFieldMasker.Redacted);
        dto.After.Should().Contain(SensitiveFieldMasker.Redacted);
        dto.Before.Should().NotContain("111222333");
        dto.Before.Should().NotContain("NID-9");
        dto.After.Should().NotContain("999888777");
        dto.After.Should().NotContain("hashed");
        dto.IpAddress.Should().Be("10.0.0.1");
        dto.UserAgent.Should().Be("Mozilla");
        dto.TraceId.Should().Be("trace-1");
        dto.ActorEmail.Should().Be("actor@a.com");
    }

    [Fact]
    public async Task Detail_ForOtherTenantRow_ReturnsNotFound()
    {
        await SeedTenantsAndUsersAsync();
        var id = BaseEntity.NewUuidV7();
        using (var db = Db(_tenantB))
        {
            db.AuditLogs.Add(new AuditLog
            {
                Id = id, TenantId = _tenantB, UserId = _otherActor,
                EventType = "B.Event", Action = "B.Event", CreatedAt = Base,
            });
            await db.SaveChangesAsync();
        }

        var result = await Service(_tenantA).GetAsync(id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── AC-4 / BR-4: export writes audit row + masked content ────────────────

    [Fact]
    public async Task Export_WritesAuditLogExportRow()
    {
        await SeedTenantsAndUsersAsync();
        await AddAuditAsync(_tenantA, Base, _actorUser, after: "{\"name\":\"Jane\"}");

        var result = await Service(_tenantA).ExportAsync(EmptyFilter(), AuditLogExportFormat.Csv);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Deferred.Should().BeFalse();

        using var db = Db(_tenantA);
        db.AuditLogs.Should().Contain(a => a.TenantId == _tenantA && a.Action == "AuditLog.Export");
    }

    [Fact]
    public async Task Export_Csv_ContainsMaskedSensitiveValues()
    {
        await SeedTenantsAndUsersAsync();
        await AddAuditAsync(_tenantA, Base, _actorUser, after: "{\"bank_account_number\":\"555\",\"name\":\"Jane\"}");

        var result = await Service(_tenantA).ExportAsync(EmptyFilter(), AuditLogExportFormat.Csv);

        var csv = Encoding.UTF8.GetString(result.Value!.Content);
        csv.Should().Contain(SensitiveFieldMasker.Redacted);
        csv.Should().NotContain("555");
        csv.Should().Contain("timestamp,actor,action,resource_type,resource_id,summary,ip_address");
    }

    [Fact]
    public async Task Export_RespectsFilters()
    {
        await SeedTenantsAndUsersAsync();
        await AddAuditAsync(_tenantA, Base, _actorUser, action: "Leave.Approve", resourceType: "Leave");
        await AddAuditAsync(_tenantA, Base.AddMinutes(1), _actorUser, action: "Employee.Update", resourceType: "Employee");

        var result = await Service(_tenantA).ExportAsync(
            EmptyFilter() with { ResourceType = "Leave" }, AuditLogExportFormat.Json);

        // 1 matching record (the export-audit row is written AFTER the snapshot, so it is not counted).
        result.Value!.RecordCount.Should().Be(1);
        var json = Encoding.UTF8.GetString(result.Value.Content);
        json.Should().Contain("Leave.Approve");
        json.Should().NotContain("Employee.Update");
    }

    // ── FR-6: retention purge ────────────────────────────────────────────────

    [Fact]
    public async Task Purge_DeletesRowsOlderThanRetention_KeepsRecent_AndLogsPurge()
    {
        await SeedTenantsAndUsersAsync();
        var now = Base;
        await AddAuditAsync(_tenantA, now.AddDays(-91), _actorUser, action: "Old.Event");   // older than 90d → purged
        await AddAuditAsync(_tenantA, now.AddDays(-89), _actorUser, action: "Recent.Event"); // within window → kept
        await AddAuditAsync(_tenantA, now, _actorUser, action: "Today.Event");               // kept

        var purge = new AuditLogPurgeService(Db(_tenantA), _purgeLogger);
        var deleted = await purge.PurgeExpiredAsync(now);

        deleted.Should().Be(1);

        using var db = Db(_tenantA);
        var actions = db.AuditLogs.Where(a => a.TenantId == _tenantA).Select(a => a.Action).ToList();
        actions.Should().NotContain("Old.Event");
        actions.Should().Contain("Recent.Event");
        actions.Should().Contain("Today.Event");
        // FR-6: the purge itself is audited.
        actions.Should().Contain("AuditLog.Purge");
    }

    [Fact]
    public async Task Purge_IsTenantScopedPerRetentionWindow()
    {
        await SeedTenantsAndUsersAsync();
        var now = Base;
        // Tenant B has a 30-day window; this 60-day-old row should be purged for B but an equivalent A row
        // (90-day window) survives.
        using (var db = Db(_tenantB))
        {
            var b = await db.Tenants.FindAsync(_tenantB);
            b!.AuditLogRetentionDays = 30;
            await db.SaveChangesAsync();
        }
        await AddAuditAsync(_tenantA, now.AddDays(-60), _actorUser, action: "A.Old");
        await AddAuditAsync(_tenantB, now.AddDays(-60), _otherActor, action: "B.Old");

        var purge = new AuditLogPurgeService(Db(_tenantA), _purgeLogger);
        await purge.PurgeExpiredAsync(now);

        using var verify = Db(_tenantA);
        verify.AuditLogs.Where(a => a.TenantId == _tenantA).Select(a => a.Action).Should().Contain("A.Old");
        verify.AuditLogs.Where(a => a.TenantId == _tenantB).Select(a => a.Action).Should().NotContain("B.Old");
    }

    [Fact]
    public async Task List_WithoutTenantContext_Fails()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var svc = new AuditLogService(Db(_tenantA), ctx, _currentUser, _logger);

        var result = await svc.ListAsync(EmptyFilter(), 1, 50);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    private static AuditLogFilter EmptyFilter() => new(null, null, null, null, null, null);
}
