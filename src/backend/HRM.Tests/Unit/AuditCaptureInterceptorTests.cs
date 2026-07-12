// ============================================================================
// US-NTF-004: AuditCaptureInterceptor — automatic generic INSERT/UPDATE/DELETE
// capture into the shared audit_log table for IAuditableEntity types.
// Covers: insert→after-only (BR-2), update→only-changed-props before/after (BR-3),
// soft-delete→Delete action (AC-3), non-auditable entity NOT captured, no
// recursion (writing AuditLog rows produces no further rows), and tenant + actor
// + IP/UA/trace enrichment (FR-7/FR-8). EF Core InMemory only.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.AuditLog;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AuditCaptureInterceptorTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _actor = Guid.NewGuid();
    private const string ActorEmail = "officer@acme.com";
    private const string Ip = "203.0.113.7";
    private const string Ua = "Mozilla/5.0 (Test)";

    private ITenantContext TenantCtx()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(_tenant);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    private ICurrentUser User()
    {
        var u = Substitute.For<ICurrentUser>();
        u.IsAuthenticated.Returns(true);
        u.UserId.Returns(_actor);
        u.Email.Returns(ActorEmail);
        return u;
    }

    private IHttpContextAccessor HttpAccessor()
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(Ip);
        http.Request.Headers.UserAgent = Ua;
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);
        return accessor;
    }

    private AppDbContext Db(string dbName, ITenantContext ctx, ICurrentUser user, IHttpContextAccessor? http)
    {
        var capture = new AuditCaptureInterceptor(ctx, user, http);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(capture)
            .Options;
        return new AppDbContext(options, ctx);
    }

    // NOTE: the TenantInterceptor is intentionally NOT wired in these tests (we test the capture
    // interceptor in isolation), so seeded entities must carry TenantId explicitly or the Department/
    // LeaveRequest global query filter would hide them on re-read.
    private Department NewDepartment(Guid id, string name = "Engineering")
        => new() { Id = id, TenantId = _tenant, Name = name, Code = "ENG", IsActive = true };

    // ── INSERT → after-only (BR-2) ────────────────────────────────────────────

    [Fact]
    public async Task Insert_creates_audit_row_with_after_only()
    {
        var dbName = Guid.NewGuid().ToString();
        var ctx = TenantCtx();
        var deptId = Guid.NewGuid();

        using (var db = Db(dbName, ctx, User(), HttpAccessor()))
        {
            db.Departments.Add(NewDepartment(deptId));
            await db.SaveChangesAsync();
        }

        using var read = Db(dbName, ctx, User(), null);
        var row = await read.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.ResourceType == "Department");

        row.Action.Should().Be("Department.Create");
        row.EventType.Should().Be("Department.Create");
        row.ResourceId.Should().Be(deptId.ToString());
        row.Before.Should().BeNull();
        row.After.Should().NotBeNull();
        row.After!.Should().Contain("Engineering");
    }

    // ── UPDATE → only changed properties in before/after (BR-3) ────────────────

    [Fact]
    public async Task Update_captures_only_changed_properties()
    {
        var dbName = Guid.NewGuid().ToString();
        var ctx = TenantCtx();
        var deptId = Guid.NewGuid();

        using (var seed = Db(dbName, ctx, User(), null))
        {
            seed.Departments.Add(NewDepartment(deptId, "Engineering"));
            await seed.SaveChangesAsync();
        }

        using (var db = Db(dbName, ctx, User(), HttpAccessor()))
        {
            var dept = await db.Departments.SingleAsync(d => d.Id == deptId);
            dept.Name = "Platform Engineering"; // only Name changes
            await db.SaveChangesAsync();
        }

        using var read = Db(dbName, ctx, User(), null);
        var update = await read.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.Action == "Department.Update");

        update.Before.Should().NotBeNull();
        update.After.Should().NotBeNull();

        var before = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(update.Before!)!;
        var after = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(update.After!)!;

        before.Should().ContainKey("Name");
        after.Should().ContainKey("Name");
        before["Name"].GetString().Should().Be("Engineering");
        after["Name"].GetString().Should().Be("Platform Engineering");

        // BR-3: unchanged props (e.g. Code) are NOT included.
        before.Should().NotContainKey("Code");
        after.Should().NotContainKey("Code");
    }

    // ── Soft-delete → Delete action (AC-3) ─────────────────────────────────────

    [Fact]
    public async Task Soft_delete_is_captured_as_delete_action()
    {
        var dbName = Guid.NewGuid().ToString();
        var ctx = TenantCtx();
        var reqId = Guid.NewGuid();

        using (var seed = Db(dbName, ctx, User(), null))
        {
            seed.LeaveRequests.Add(new LeaveRequest
            {
                Id = reqId,
                TenantId = _tenant,
                EmployeeId = Guid.NewGuid(),
                LeaveTypeId = Guid.NewGuid(),
                StartDate = new DateOnly(2026, 6, 1),
                EndDate = new DateOnly(2026, 6, 2),
                TotalDays = 2,
                Status = LeaveRequestStatus.Pending,
            });
            await seed.SaveChangesAsync();
        }

        using (var db = Db(dbName, ctx, User(), HttpAccessor()))
        {
            var req = await db.LeaveRequests.SingleAsync(r => r.Id == reqId);
            req.IsDeleted = true; // soft-delete: false → true
            await db.SaveChangesAsync();
        }

        using var read = Db(dbName, ctx, User(), null);
        var row = await read.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.Action == "LeaveRequest.Delete");

        var before = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Before!)!;
        var after = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.After!)!;
        before["IsDeleted"].GetBoolean().Should().BeFalse();
        after["IsDeleted"].GetBoolean().Should().BeTrue();
    }

    // ── Non-auditable entity is NOT captured ──────────────────────────────────

    [Fact]
    public async Task Non_auditable_entity_is_not_captured()
    {
        var dbName = Guid.NewGuid().ToString();
        var ctx = TenantCtx();

        using (var db = Db(dbName, ctx, User(), HttpAccessor()))
        {
            // Holiday does NOT implement IAuditableEntity.
            db.Holidays.Add(new Holiday
            {
                Id = Guid.NewGuid(),
                Name = "New Year",
                Date = new DateOnly(2026, 1, 1),
            });
            await db.SaveChangesAsync();
        }

        using var read = Db(dbName, ctx, User(), null);
        (await read.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.ResourceType == "Holiday"))
            .Should().BeFalse();
    }

    // ── No recursion: writing AuditLog rows produces no further audit rows ─────

    [Fact]
    public async Task Audit_writes_do_not_recurse()
    {
        var dbName = Guid.NewGuid().ToString();
        var ctx = TenantCtx();

        using (var db = Db(dbName, ctx, User(), HttpAccessor()))
        {
            // Two auditable inserts in one save → exactly two audit rows (no audit-of-audit).
            db.Departments.Add(NewDepartment(Guid.NewGuid(), "A"));
            db.Departments.Add(NewDepartment(Guid.NewGuid(), "B"));
            await db.SaveChangesAsync();
        }

        using var read = Db(dbName, ctx, User(), null);
        var total = await read.AuditLogs.IgnoreQueryFilters().CountAsync();
        total.Should().Be(2);

        // And none of the rows targets the AuditLog type itself.
        (await read.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.ResourceType == "AuditLog"))
            .Should().BeFalse();
    }

    // ── Enrichment: tenant + actor + IP + UA + trace (FR-7/FR-8/AC-1) ──────────

    [Fact]
    public async Task Audit_row_is_enriched_with_tenant_actor_ip_ua()
    {
        var dbName = Guid.NewGuid().ToString();
        var ctx = TenantCtx();

        using (var db = Db(dbName, ctx, User(), HttpAccessor()))
        {
            db.Departments.Add(NewDepartment(Guid.NewGuid()));
            await db.SaveChangesAsync();
        }

        using var read = Db(dbName, ctx, User(), null);
        var row = await read.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.ResourceType == "Department");

        row.TenantId.Should().Be(_tenant);
        row.UserId.Should().Be(_actor);
        row.ActorEmployeeNo.Should().Be(ActorEmail);
        row.IpAddress.Should().Be(Ip);
        row.UserAgent.Should().Be(Ua);
        row.TraceId.Should().NotBeNullOrEmpty();
    }

    // ── BUG-281: WRITE-TIME PII redaction of the serialized before/after JSONB ──
    // Once BUG-082 broadens auto-audit to PII-bearing entities (Employee, bank/national-id/salary),
    // those values must NEVER be persisted in cleartext. Redaction is applied inside SerializeAll (the
    // INSERT/DELETE snapshot) and SerializeChanged (the UPDATE before+after), BEFORE the JSON is stored.
    // Driven directly against a real EF EntityEntry (the internal serializers) with a probe entity that
    // carries sensitive-named scalar props — none of the 3 currently auto-audited entities carry PII.

    private sealed class PiiProbe
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;   // non-sensitive → must pass through
        public string? NationalId { get; set; }
        public string? BankAccountNumber { get; set; }
        public decimal Salary { get; set; }
    }

    private sealed class ProbeDbContext : DbContext
    {
        public ProbeDbContext(DbContextOptions<ProbeDbContext> options) : base(options) { }
        public DbSet<PiiProbe> Probes => Set<PiiProbe>();
    }

    private static ProbeDbContext ProbeDb(string dbName)
        => new(new DbContextOptionsBuilder<ProbeDbContext>().UseInMemoryDatabase(dbName).Options);

    [Fact]
    public void SerializeAll_redacts_sensitive_pii_values_at_write_time()
    {
        using var db = ProbeDb(Guid.NewGuid().ToString());
        var probe = new PiiProbe
        {
            Id = Guid.NewGuid(),
            Name = "Jane Doe",
            NationalId = "NID-42",
            BankAccountNumber = "9988776655",
            Salary = 125000m,
        };
        db.Probes.Add(probe); // State = Added → SerializeAll uses CurrentValue

        var json = AuditCaptureInterceptor.SerializeAll(db.Entry(probe))!;
        var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        map["NationalId"].GetString().Should().Be(SensitiveFieldMasker.Redacted);
        map["BankAccountNumber"].GetString().Should().Be(SensitiveFieldMasker.Redacted);
        map["Salary"].GetString().Should().Be(SensitiveFieldMasker.Redacted);
        // Non-sensitive field passes through untouched.
        map["Name"].GetString().Should().Be("Jane Doe");
        // Cleartext PII must not survive anywhere in the persisted payload.
        json.Should().NotContain("9988776655").And.NotContain("NID-42").And.NotContain("125000");
    }

    [Fact]
    public void SerializeChanged_redacts_sensitive_pii_values_in_before_and_after()
    {
        var dbName = Guid.NewGuid().ToString();
        var probeId = Guid.NewGuid();

        using (var seed = ProbeDb(dbName))
        {
            seed.Probes.Add(new PiiProbe
            {
                Id = probeId,
                Name = "Jane",
                NationalId = "NID-1",
                BankAccountNumber = "1111",
                Salary = 100m,
            });
            seed.SaveChanges();
        }

        using var db = ProbeDb(dbName);
        var probe = db.Probes.Single(p => p.Id == probeId);
        probe.Name = "Janet";                 // non-sensitive change
        probe.NationalId = "NID-2";
        probe.BankAccountNumber = "2222";
        probe.Salary = 200m;

        var (before, after) = AuditCaptureInterceptor.SerializeChanged(db.Entry(probe));

        var beforeMap = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(before!)!;
        var afterMap = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(after!)!;

        beforeMap["NationalId"].GetString().Should().Be(SensitiveFieldMasker.Redacted);
        afterMap["NationalId"].GetString().Should().Be(SensitiveFieldMasker.Redacted);
        beforeMap["BankAccountNumber"].GetString().Should().Be(SensitiveFieldMasker.Redacted);
        afterMap["BankAccountNumber"].GetString().Should().Be(SensitiveFieldMasker.Redacted);
        beforeMap["Salary"].GetString().Should().Be(SensitiveFieldMasker.Redacted);
        afterMap["Salary"].GetString().Should().Be(SensitiveFieldMasker.Redacted);

        // Non-sensitive field keeps its real old/new values.
        beforeMap["Name"].GetString().Should().Be("Jane");
        afterMap["Name"].GetString().Should().Be("Janet");

        // No cleartext PII in either side of the diff.
        before.Should().NotContain("NID-1").And.NotContain("1111");
        after.Should().NotContain("NID-2").And.NotContain("2222");
    }

    [Fact]
    public async Task Audited_non_pii_entity_serializes_fields_without_redaction()
    {
        // The 3 currently auto-audited entities (Department/JobTitle/LeaveRequest) carry no PII, so their
        // real serialized values must survive intact — the write-time masker must not over-redact.
        var dbName = Guid.NewGuid().ToString();
        var ctx = TenantCtx();

        using (var db = Db(dbName, ctx, User(), HttpAccessor()))
        {
            db.Departments.Add(NewDepartment(Guid.NewGuid(), "Engineering"));
            await db.SaveChangesAsync();
        }

        using var read = Db(dbName, ctx, User(), null);
        var row = await read.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.ResourceType == "Department");

        row.After!.Should().Contain("Engineering");
        row.After!.Should().NotContain(SensitiveFieldMasker.Redacted);
    }
}
