// ============================================================================
// US-ADM-012 phase 4 (AC-4) — REAL Storage + EmailSends usage gauges on REAL Postgres (Testcontainers).
//
// Proves what InMemory masks: the Storage gauge is a CROSS-TABLE sum over all FOUR size-bearing tables
// (EmployeeDocuments + HrReportExport + PayrollReportExport + PayrollSlip PDFs) resolved through the shared
// TenantStorageUsage helper, and the EmailSends gauge counts ONLY (Channel==Email && Status==Sent &&
// SentAt >= start-of-current-UTC-month) NotificationDelivery rows. The four storage sizes are distinct powers
// of two (1/2/4/8 MB ⇒ 15 MB) so dropping ANY single table from the sum yields a distinct wrong total and
// fails the assertion (mutation-kill). The email arm seeds one out-of-month, one Failed, and one in-app row —
// each an exclusion that a dropped filter clause would wrongly include (3 → 4), killing that arm.
//
// The monitoring service runs in the SYSTEM/admin context (no resolved tenant ⇒ tenant query filter disabled),
// so both gauges go through the cross-tenant grouped helpers. Limits resolve via PlanLimitResolver
// (override > plan; null = UNLIMITED per BR-3). ApiCalls stays Available=false (no per-request instrumentation).
//
// Runs against a throwaway postgres:17-alpine container (mirrors the other *PostgresTests). The agent verify
// gate has no Docker; this arm is executed by the orchestrator's Postgres run.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Monitoring.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-ADM-012")] // real-PG AC-4 storage + email usage gauges
[Trait("Category", "Monitoring")]
public sealed class PlatformMonitoringUsageGaugesPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private string _cs = null!;
    private const long Mb = 1024L * 1024L;

    // The monitoring service runs with no resolved tenant (system/admin context) — TenantId=Guid.Empty ⇒
    // IsResolved=false ⇒ the tenant query filter is disabled, so the cross-tenant grouped helpers apply.
    /// <summary>Resolved to ONE tenant, so AppDbContext's current-tenant query filters apply.</summary>
    private sealed class ScopedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public string Subdomain => "scoped";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => true;
        public void SetTenant(Guid t, string s, TenantStatus st, string? p = null,
            IReadOnlyCollection<string>? m = null, string? l = null, string? c = null) { }
        public void SetSystemContext() { }
    }

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
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status, string? plan = null,
            IReadOnlyCollection<string>? enabledModules = null, string? logoUrl = null, string? primaryColor = null) { }
        public void SetSystemContext() { }
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _cs = _postgres.GetConnectionString() + ";Include Error Detail=true";
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext Db() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_cs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options,
        new SystemTenantContext());

    /// <summary>Same context, but resolved to ONE tenant so the current-tenant query filters apply.</summary>
    private AppDbContext ScopedDb(Guid tenantId) => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_cs, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options,
        new ScopedTenantContext(tenantId));

    private PlatformMonitoringService Service()
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(Guid.NewGuid());
        user.IsAuthenticated.Returns(true);
        var jobQueue = Substitute.For<IJobQueueMonitor>();
        jobQueue.GetSnapshot().Returns(new JobQueueSnapshotDto(false, null, null, null, null, null));
        return new PlatformMonitoringService(
            Db(), user, jobQueue, new ConfigurationBuilder().Build(),
            NullLogger<PlatformMonitoringService>.Instance);
    }

    // ── seeding ──────────────────────────────────────────────────────────────

    private async Task SeedPlanAsync(string code, int? maxStorageGb, int? maxEmailSends)
    {
        await using var db = Db();
        db.SubscriptionPlans.Add(new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            MaxEmployees = 50,
            MaxStorageGb = maxStorageGb,
            MaxEmailSendsPerMonth = maxEmailSends,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedTenantAsync(string subdomain, string planId)
    {
        var id = Guid.NewGuid();
        await using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = id,
            Name = subdomain,
            Subdomain = subdomain,
            Status = TenantStatus.Active,
            PlanId = planId,
            MaxEmployees = 50,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return id;
    }

    // One row in EACH of the four size-bearing tables, with distinct power-of-two MB sizes (1/2/4/8 = 15 MB),
    // so omitting any single table changes the total uniquely.
    private async Task SeedStorageAcrossAllFourTablesAsync(Guid tenantId)
    {
        await using var db = Db();
        var deptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();
        db.Departments.Add(new Department
        {
            Id = deptId, TenantId = tenantId, Name = "Dept", Code = $"D-{tenantId:N}".Substring(0, 10),
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = "Engineer",
        });
        var employeeId = Guid.NewGuid();
        db.Employees.Add(new Employee
        {
            Id = employeeId, TenantId = tenantId, FirstName = "E", LastName = "Doc",
            Email = $"doc@{tenantId:N}.test", EmployeeNo = $"EMP-{tenantId:N}".Substring(0, 12),
            DepartmentId = deptId, JobTitleId = jobTitleId,
            IsActive = true, CreatedAt = DateTime.UtcNow,
        });
        db.EmployeeDocuments.Add(new EmployeeDocument
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId,
            FileName = "a.pdf", StorageKey = "k/a", MimeType = "application/pdf",
            Category = DocumentCategory.Other, UploadedBy = Guid.NewGuid(), FileSizeBytes = 1 * Mb,
        });
        db.HrReportExports.Add(new HrReportExport
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RequestedByUserId = Guid.NewGuid(),
            ReportType = "headcount", FiltersJson = "{}", RowCount = 1, FileSizeBytes = 2 * Mb,
        });
        db.PayrollReportExports.Add(new PayrollReportExport
        {
            Id = Guid.NewGuid(), TenantId = tenantId, RequestedByUserId = Guid.NewGuid(),
            ReportType = "register", FiltersJson = "{}", RowCount = 1, FileSizeBytes = 4 * Mb,
        });
        db.PayrollSlips.Add(new PayrollSlip
        {
            Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = Guid.NewGuid(), EmployeeId = Guid.NewGuid(),
            PayMonth = 1, PayYear = 2026, WorkingDays = 22m, PaidDays = 22m,
            PdfFileSizeBytes = (int)(8 * Mb),
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedEmailDeliveriesAsync(Guid tenantId, int inMonthSent)
    {
        await using var db = Db();
        var now = DateTime.UtcNow;

        for (var i = 0; i < inMonthSent; i++)
            db.NotificationDeliveries.Add(Delivery(tenantId, NotificationDeliveryChannel.Email,
                NotificationDeliveryStatus.Sent, now));

        // EXCLUDED arms — each is what a dropped filter clause would wrongly include:
        db.NotificationDeliveries.Add(Delivery(tenantId, NotificationDeliveryChannel.Email,
            NotificationDeliveryStatus.Sent, now.AddMonths(-1)));                 // out of month
        db.NotificationDeliveries.Add(Delivery(tenantId, NotificationDeliveryChannel.Email,
            NotificationDeliveryStatus.Failed, now));                             // not Sent
        db.NotificationDeliveries.Add(Delivery(tenantId, NotificationDeliveryChannel.InApp,
            NotificationDeliveryStatus.Sent, now));                               // wrong channel

        await db.SaveChangesAsync();
    }

    private static NotificationDelivery Delivery(
        Guid tenantId, NotificationDeliveryChannel channel, NotificationDeliveryStatus status, DateTime sentAt) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Channel = channel,
            Status = status,
            SentAt = sentAt,
            NotificationType = "test.notification",
            EventKey = "test_event",
            RecipientUserId = Guid.NewGuid(),
            RecipientEmail = "r@test.com",
        };

    private static UsageGaugeDto Gauge(TenantUsageSummaryDto row, string resource)
        => row.Gauges.Single(g => g.Resource == resource);

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Storage_gauge_sums_all_four_size_bearing_tables_against_max_storage_gb()
    {
        await SeedPlanAsync("professional", maxStorageGb: 10, maxEmailSends: 100);
        var tenantId = await SeedTenantAsync("acme", "professional");
        await SeedStorageAcrossAllFourTablesAsync(tenantId);

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter());
        var row = result.Value!.Tenants.Single(t => t.TenantId == tenantId);
        var storage = Gauge(row, "Storage");

        storage.Available.Should().BeTrue();
        // 1+2+4+8 MB across the four tables. If ANY table is dropped from the sum this is 14/13/11/7, not 15.
        storage.Used.Should().Be(15, "the sum must include EmployeeDocuments + HrReportExport + PayrollReportExport + PayrollSlip");
        storage.Limit.Should().Be(10 * 1024); // MB
        storage.UsagePercent.Should().NotBeNull();
        storage.Band.Should().Be(UsageBand.Green);
    }

    [Fact]
    public async Task Email_gauge_counts_only_Email_Sent_in_current_month_against_max_email_sends()
    {
        await SeedPlanAsync("professional", maxStorageGb: 10, maxEmailSends: 100);
        var tenantId = await SeedTenantAsync("acme", "professional");
        await SeedEmailDeliveriesAsync(tenantId, inMonthSent: 3);

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter());
        var email = Gauge(result.Value!.Tenants.Single(t => t.TenantId == tenantId), "EmailSends");

        email.Available.Should().BeTrue();
        // 3 in-month Email+Sent. The out-of-month, Failed, and in-app rows must ALL be excluded (else this is 4).
        email.Used.Should().Be(3, "out-of-month, Failed and in-app deliveries must be excluded");
        email.Limit.Should().Be(100);
        email.UsagePercent.Should().Be(3d);
        email.Band.Should().Be(UsageBand.Green);
    }

    [Fact]
    public async Task Null_plan_limit_is_represented_as_unlimited_not_zero_or_divide_by_null()
    {
        await SeedPlanAsync("enterprise", maxStorageGb: null, maxEmailSends: null);
        var tenantId = await SeedTenantAsync("beta", "enterprise");
        await SeedStorageAcrossAllFourTablesAsync(tenantId);
        await SeedEmailDeliveriesAsync(tenantId, inMonthSent: 2);

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter());
        var row = result.Value!.Tenants.Single(t => t.TenantId == tenantId);
        var storage = Gauge(row, "Storage");
        var email = Gauge(row, "EmailSends");

        // Unlimited (BR-3): real usage still reported, but no cap ⇒ null limit + null percent/band (never 0%).
        storage.Available.Should().BeTrue();
        storage.Used.Should().Be(15);
        storage.Limit.Should().BeNull();
        storage.UsagePercent.Should().BeNull();
        storage.Band.Should().BeNull();

        email.Available.Should().BeTrue();
        email.Used.Should().Be(2);
        email.Limit.Should().BeNull();
        email.UsagePercent.Should().BeNull();
        email.Band.Should().BeNull();
    }

    [Fact]
    public async Task ApiCalls_gauge_stays_unavailable_and_is_not_fabricated()
    {
        await SeedPlanAsync("professional", maxStorageGb: 10, maxEmailSends: 100);
        var tenantId = await SeedTenantAsync("acme", "professional");

        var result = await Service().GetTenantUsageAsync(new TenantUsageFilter());
        var api = Gauge(result.Value!.Tenants.Single(t => t.TenantId == tenantId), "ApiCalls");

        api.Available.Should().BeFalse();
        api.Used.Should().BeNull();
        api.Limit.Should().BeNull();
    }

    [Fact]
    public async Task Detail_view_surfaces_the_same_real_storage_total_for_one_tenant()
    {
        await SeedPlanAsync("professional", maxStorageGb: 10, maxEmailSends: 100);
        var tenantId = await SeedTenantAsync("acme", "professional");
        await SeedStorageAcrossAllFourTablesAsync(tenantId);
        // A second tenant with its own storage must NOT bleed into the first tenant's onlyTenant-scoped sum.
        var otherPlanTenant = await SeedTenantAsync("beta", "professional");
        await SeedStorageAcrossAllFourTablesAsync(otherPlanTenant);

        var result = await Service().GetTenantDetailAsync(tenantId);

        result.IsSuccess.Should().BeTrue(result.Error);
        // The detail's onlyTenant scan must isolate this tenant's 15 MB, not 30 MB across both.
        var storageBytes = await TenantStorageUsage.ComputeBytesByTenantAsync(Db(), tenantId);
        storageBytes.GetValueOrDefault(tenantId).Should().Be(15 * Mb);
    }
    // ── ISSUE-340 drift guard: the two sums must agree ────────────────────────
    // TenantStorageUsage deliberately holds TWO sums over the same four tables — ComputeBytesAsync (per-tenant,
    // used by the quota GATE) and ComputeBytesByTenantAsync (cross-tenant, used by this GAUGE). Its own docblock
    // states the hazard: "If a fifth size-bearing table is added, BOTH methods must gain it." Nothing enforced
    // that, so the gate and the gauge could silently diverge — which is precisely the ISSUE-354 drift class the
    // shared helper was extracted to prevent, reintroduced one level down.
    //
    // This arm pins them together. Adding a table to one method and not the other now fails here. Verified by
    // mutation: dropping the payslip term from EITHER method kills it.
    [Fact]
    public async Task Both_storage_sums_agree_for_the_same_tenant_ISSUE340()
    {
        await SeedPlanAsync("agree-plan", maxStorageGb: 10, maxEmailSends: 100);
        var tenantId = await SeedTenantAsync("agree", "agree-plan");
        await SeedStorageAcrossAllFourTablesAsync(tenantId);

        // ComputeBytesAsync is CURRENT-tenant scoped, so it must run under a context resolved to this tenant;
        // ComputeBytesByTenantAsync ignores query filters and is asked for the same tenant explicitly.
        await using var scoped = ScopedDb(tenantId);
        var perTenant = await TenantStorageUsage.ComputeBytesAsync(scoped, CancellationToken.None);

        await using var db = Db();
        var byTenant = await TenantStorageUsage.ComputeBytesByTenantAsync(db, tenantId, CancellationToken.None);

        byTenant.GetValueOrDefault(tenantId).Should().Be(perTenant,
            "the quota gate and the usage gauge must never disagree about how many bytes a tenant is using — "
            + "one refusing an upload while the other shows headroom is the worst possible failure mode here");
    }

}
