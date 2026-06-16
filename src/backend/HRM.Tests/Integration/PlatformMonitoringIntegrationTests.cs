// ============================================================================
// US-ADM-002: System Admin monitors platform health and tenant usage — integration tests.
//
// Exercises PlatformMonitoringService over a real AppDbContext (InMemory) in the SYSTEM/admin context (no
// resolved tenant — the service uses IgnoreQueryFilters and groups by TenantId), covering the cross-cutting
// behaviour the pure-classifier unit tests cannot:
//   - AC-1: platform health aggregates tenants-by-status + active tenant/user counts from real data; the DB
//           probe reports Healthy on a live (InMemory) context; Redis reports NotConfigured (no client wired);
//           the overall roll-up is Healthy/Degraded as the signals dictate.
//   - AC-2/FR-3: per-tenant employee usage gauge + quota-breach queue. MaxEmployees=5 with 4 active employees
//                ⇒ 80% Amber and IN the breach queue (Test Hints); 5 ⇒ 100% Breached; a tenant under 80% is
//                NOT in the breach queue.
//   - cross-tenant correctness: tenant A's employee count never bleeds into tenant B's gauge.
//   - AC-5/NFR-5: each read writes a Monitoring.Viewed / Monitoring.TenantViewed audit row.
//   - BR-2 (PII): the usage/health DTOs structurally carry NO employee-name fields (asserted via reflection).
//   - AC-4: tenant detail returns owner email + last-activity + employee gauge; 404 for an unknown tenant.
//
// Hangfire (FR-5) is abstracted behind IJobQueueMonitor and SUBSTITUTED here — the tests never depend on a
// running Hangfire server. PROVIDER: InMemory — same rationale as the sibling integration tests (the verify
// gate runs `dotnet test` with no PostgreSQL / Docker).
// ============================================================================

using System.Reflection;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Monitoring.DTOs;
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

public sealed class PlatformMonitoringIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _actorUserId = Guid.NewGuid();

    // The monitoring service runs in the system/admin context: no tenant is resolved.
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

    private PlatformMonitoringService Service(IJobQueueMonitor? jobQueue = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(_actorUserId);
        user.IsAuthenticated.Returns(true);
        user.Email.Returns("sysadmin@platform.test");

        var config = new ConfigurationBuilder().Build();

        jobQueue ??= UnavailableJobQueue();

        return new PlatformMonitoringService(
            Db(), user, jobQueue, config, NullLogger<PlatformMonitoringService>.Instance);
    }

    private static IJobQueueMonitor UnavailableJobQueue()
    {
        var monitor = Substitute.For<IJobQueueMonitor>();
        monitor.GetSnapshot().Returns(new JobQueueSnapshotDto(
            Available: false, Enqueued: null, Processing: null, Scheduled: null, Succeeded: null, Failed: null));
        return monitor;
    }

    private static IJobQueueMonitor JobQueueWithFailed(long failed)
    {
        var monitor = Substitute.For<IJobQueueMonitor>();
        monitor.GetSnapshot().Returns(new JobQueueSnapshotDto(
            Available: true, Enqueued: 0, Processing: 0, Scheduled: 0, Succeeded: 100, Failed: failed));
        return monitor;
    }

    // ── seeding helpers ──────────────────────────────────────────────────────

    private async Task<Guid> SeedTenantAsync(
        string name, string subdomain, TenantStatus status, int? maxEmployees, string planId = "professional")
    {
        var id = Guid.NewGuid();
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = id,
            Name = name,
            Subdomain = subdomain,
            Status = status,
            PlanId = planId,
            MaxEmployees = maxEmployees,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedActiveEmployeesAsync(Guid tenantId, int count)
    {
        using var db = Db();
        for (var i = 0; i < count; i++)
        {
            db.Employees.Add(new Employee
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FirstName = $"E{i}",
                LastName = "Test",
                Email = $"e{i}@{tenantId:N}.test",
                EmployeeNo = $"EMP-{i:0000}",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task SeedActiveUsersAsync(int activeCount, int inactiveCount)
    {
        using var db = Db();
        for (var i = 0; i < activeCount; i++)
            db.Users.Add(new User { Id = Guid.NewGuid(), Email = $"active{i}@u.test", IsActive = true, CreatedAt = DateTime.UtcNow });
        for (var i = 0; i < inactiveCount; i++)
            db.Users.Add(new User { Id = Guid.NewGuid(), Email = $"inactive{i}@u.test", IsActive = false, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    // ── AC-1: platform health aggregation ────────────────────────────────────

    [Fact]
    public async Task Health_AggregatesTenantStatusCountsAndActiveUsers()
    {
        await SeedTenantAsync("Acme", "acme", TenantStatus.Active, maxEmployees: 50);
        await SeedTenantAsync("Beta", "beta", TenantStatus.Trial, maxEmployees: 50);
        await SeedTenantAsync("Gone", "gone", TenantStatus.Suspended, maxEmployees: 50);
        await SeedActiveUsersAsync(activeCount: 7, inactiveCount: 3);

        var result = await Service().GetPlatformHealthAsync();

        result.IsSuccess.Should().BeTrue(result.Error);
        var dto = result.Value!;

        dto.ActiveTenantCount.Should().Be(2); // Active + Trial
        dto.TotalActiveUsers.Should().Be(7);
        dto.DatabaseHealth.Should().Be(DependencyHealthStatus.Healthy);
        dto.RedisHealth.Should().Be(DependencyHealthStatus.NotConfigured); // no Redis client wired
        dto.OverallStatus.Should().Be(PlatformHealthStatus.Healthy);

        dto.TenantsByStatus.Should().ContainSingle(s => s.Status == "Active" && s.Count == 1);
        dto.TenantsByStatus.Should().ContainSingle(s => s.Status == "Trial" && s.Count == 1);
        dto.TenantsByStatus.Should().ContainSingle(s => s.Status == "Suspended" && s.Count == 1);

        // Deferred metrics are honestly null + flagged.
        dto.AggregateErrorRatePercent.Should().BeNull();
        dto.P95LatencyMs.Should().BeNull();
        dto.MetricsStatus.Should().Be(MonitoringStatus.RequiresObservabilityPipeline);
    }

    [Fact]
    public async Task Health_HighHangfireFailedCount_RollsUpToDegraded()
    {
        await SeedTenantAsync("Acme", "acme", TenantStatus.Active, maxEmployees: 50);

        var result = await Service(JobQueueWithFailed(failed: 75)).GetPlatformHealthAsync();

        result.Value!.OverallStatus.Should().Be(PlatformHealthStatus.Degraded);
        result.Value.JobQueue.Available.Should().BeTrue();
        result.Value.JobQueue.Failed.Should().Be(75);
    }

    [Fact]
    public async Task Health_WritesMonitoringViewedAudit()
    {
        await SeedTenantAsync("Acme", "acme", TenantStatus.Active, maxEmployees: 50);

        await Service().GetPlatformHealthAsync();

        using var db = Db();
        (await db.AuditLogs.IgnoreQueryFilters().AnyAsync(a => a.Action == "Monitoring.Viewed"))
            .Should().BeTrue();
    }

    // ── AC-2/FR-3: employee usage gauge + quota-breach queue ─────────────────

    [Fact]
    public async Task Usage_FourOfFiveEmployees_Is80PercentAmberAndInBreachQueue()
    {
        var tenantId = await SeedTenantAsync("Acme", "acme", TenantStatus.Active, maxEmployees: 5);
        await SeedActiveEmployeesAsync(tenantId, 4);

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter());

        result.IsSuccess.Should().BeTrue(result.Error);
        var row = result.Value!.Tenants.Single(t => t.TenantId == tenantId);

        row.ActiveEmployees.Should().Be(4);
        row.EmployeeLimit.Should().Be(5);
        row.UsagePercent.Should().Be(80d);
        row.Band.Should().Be(UsageBand.Amber);

        result.Value.QuotaBreachQueue.Should().Contain(t => t.TenantId == tenantId);
    }

    [Fact]
    public async Task Usage_FiveOfFiveEmployees_Is100PercentBreached()
    {
        var tenantId = await SeedTenantAsync("Acme", "acme", TenantStatus.Active, maxEmployees: 5);
        await SeedActiveEmployeesAsync(tenantId, 5);

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter());

        var row = result.Value!.Tenants.Single(t => t.TenantId == tenantId);
        row.UsagePercent.Should().Be(100d);
        row.Band.Should().Be(UsageBand.Breached);
        result.Value.QuotaBreachQueue.Should().Contain(t => t.TenantId == tenantId);
    }

    [Fact]
    public async Task Usage_TenantUnder80Percent_NotInBreachQueue()
    {
        var tenantId = await SeedTenantAsync("Acme", "acme", TenantStatus.Active, maxEmployees: 10);
        await SeedActiveEmployeesAsync(tenantId, 3); // 30%

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter());

        var row = result.Value!.Tenants.Single(t => t.TenantId == tenantId);
        row.Band.Should().Be(UsageBand.Green);
        result.Value.QuotaBreachQueue.Should().NotContain(t => t.TenantId == tenantId);
    }

    // ── cross-tenant correctness ─────────────────────────────────────────────

    [Fact]
    public async Task Usage_EmployeeCountsDoNotBleedAcrossTenants()
    {
        var tenantA = await SeedTenantAsync("Acme", "acme", TenantStatus.Active, maxEmployees: 10);
        var tenantB = await SeedTenantAsync("Beta", "beta", TenantStatus.Active, maxEmployees: 10);
        await SeedActiveEmployeesAsync(tenantA, 8);
        await SeedActiveEmployeesAsync(tenantB, 2);

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter());

        result.Value!.Tenants.Single(t => t.TenantId == tenantA).ActiveEmployees.Should().Be(8);
        result.Value.Tenants.Single(t => t.TenantId == tenantB).ActiveEmployees.Should().Be(2);
        // Only tenant A (80%) is in the breach queue; tenant B (20%) is not.
        result.Value.QuotaBreachQueue.Select(t => t.TenantId).Should().BeEquivalentTo(new[] { tenantA });
    }

    // ── FR-4 filters ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Usage_StatusFilter_ReturnsOnlyMatchingTenants()
    {
        await SeedTenantAsync("Acme", "acme", TenantStatus.Active, maxEmployees: 10);
        await SeedTenantAsync("Beta", "beta", TenantStatus.Suspended, maxEmployees: 10);

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter(Status: "Suspended"));

        result.Value!.Tenants.Should().ContainSingle()
            .Which.Subdomain.Should().Be("beta");
    }

    // ── AC-4: tenant detail ──────────────────────────────────────────────────

    [Fact]
    public async Task Detail_ReturnsOwnerEmailLastActivityAndEmployeeGauge_AndAudits()
    {
        var tenantId = await SeedTenantAsync("Acme", "acme", TenantStatus.Active, maxEmployees: 5);
        await SeedActiveEmployeesAsync(tenantId, 4);
        await SeedOwnerAsync(tenantId, "owner@acme.com");
        await SeedTenantAuditRowAsync(tenantId); // a tenant-scoped activity row ⇒ last-activity is derivable

        var result = await Service().GetTenantDetailAsync(tenantId);

        result.IsSuccess.Should().BeTrue(result.Error);
        var dto = result.Value!;
        dto.OwnerEmail.Should().Be("owner@acme.com");
        dto.EmployeeUsage.Used.Should().Be(4);
        dto.EmployeeUsage.Limit.Should().Be(5);
        dto.EmployeeUsage.Band.Should().Be(UsageBand.Amber);
        dto.LastActivityAt.Should().NotBeNull(); // the audit write from a prior read leaves a row

        // Deferred fields are honestly empty/null + flagged.
        dto.ErrorRateTrend24h.Should().BeEmpty();
        dto.SlaUptimePercent.Should().BeNull();
        dto.MetricsStatus.Should().Be(MonitoringStatus.RequiresObservabilityPipeline);

        using var db = Db();
        (await db.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.Action == "Monitoring.TenantViewed" && a.ResourceId == tenantId.ToString()))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Detail_UnknownTenant_Returns404()
    {
        var result = await Service().GetTenantDetailAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("tenant_not_found");
    }

    // ── BR-2: no PII in the monitoring DTOs (structural) ─────────────────────

    [Fact]
    public void UsageAndHealthDtos_CarryNoEmployeePiiFields()
    {
        // The usage/health DTOs must expose only aggregates/counts and operational fields — never per-employee
        // PII. Asserted structurally: no property named after an employee PII field exists on any monitoring DTO.
        var piiNames = new[] { "FirstName", "LastName", "FullName", "EmployeeName", "Salary", "Ssn", "NationalId", "Dob", "DateOfBirth" };

        var dtoTypes = new[]
        {
            typeof(PlatformHealthDto), typeof(TenantUsageSummaryDto), typeof(TenantUsageDashboardDto),
            typeof(UsageGaugeDto), typeof(TenantMonitoringDetailDto),
        };

        foreach (var type in dtoTypes)
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);
            props.Should().NotIntersectWith(piiNames, $"{type.Name} must not expose employee PII (BR-2)");
        }
    }

    private async Task SeedTenantAuditRowAsync(Guid tenantId)
    {
        using var db = Db();
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            EventType = "Employee.Created",
            Action = "Employee.Created",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedOwnerAsync(Guid tenantId, string email)
    {
        using var db = Db();
        var user = new User { Id = Guid.NewGuid(), Email = email, IsActive = true, CreatedAt = DateTime.UtcNow };
        var ut = new UserTenant { Id = Guid.NewGuid(), UserId = user.Id, TenantId = tenantId, Status = UserTenantStatus.Active, CreatedAt = DateTime.UtcNow };
        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = PermissionCatalog.BuiltInRoles.TenantOwner,
            IsBuiltIn = true,
            CreatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        db.UserTenants.Add(ut);
        db.Roles.Add(role);
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = ut.Id, RoleId = role.Id, AssignedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }
}
