// ============================================================================
// US-RPT-004 / BR-3: the export cleanup sweep, on REAL POSTGRES (E3, Leg-3 parity).
//
// Split out of HrReportExportPostgresTests, unchanged. It is the ONE arm in that suite that queries
// CROSS-TENANT (`IgnoreQueryFilters`) and asserts a GLOBAL count, so it cannot share a database with
// sibling tests — a sibling that seeds its own overdue export would change the number. It therefore
// keeps the repo's original one-container-per-test shape (IAsyncLifetime) rather than the shared
// PostgresContainerFixture, which buys the assertion an empty table without weakening it.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-RPT-004-PG")]
public sealed class HrReportExportCleanupPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        using var db = Db(_tenantA);
        await db.Database.MigrateAsync();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantA, Subdomain = "cleanup-a", Name = "Cleanup A",
            DefaultCountryCode = "LK", FiscalYearStartMonth = 1,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

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
        public void SetSystemContext() => TenantId = Guid.Empty;
    }

    private DbContextOptions<AppDbContext> NpgsqlOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

    private AppDbContext Db(Guid tenantId) =>
        new(NpgsqlOptions(), new MutableTenantContext { TenantId = tenantId });

    // ── BR-3 cleanup: expires overdue completed exports ──────────────────────

    [Fact]
    public async Task CleanupService_ExpiresOverdueCompletedExports()
    {
        using (var seed = Db(_tenantA))
        {
            seed.HrReportExports.Add(new HrReportExport
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, RequestedByUserId = _userA,
                ReportType = "headcount", Format = HrReportExportFormat.Csv, FiltersJson = "{}",
                Status = HrReportExportStatus.Completed, RowCount = 5, FileSizeBytes = 100,
                FilePath = null,
                RequestedAt = DateTime.UtcNow.AddDays(-8), CompletedAt = DateTime.UtcNow.AddDays(-8),
                ExpiresAt = DateTime.UtcNow.AddDays(-1), // overdue
            });
            seed.HrReportExports.Add(new HrReportExport
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, RequestedByUserId = _userA,
                ReportType = "headcount", Format = HrReportExportFormat.Csv, FiltersJson = "{}",
                Status = HrReportExportStatus.Completed, RowCount = 5, FileSizeBytes = 100,
                FilePath = null,
                RequestedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(6), // still fresh
            });
            await seed.SaveChangesAsync();
        }

        // Cleanup runs in the system context (cross-tenant).
        var ctx = new MutableTenantContext(); // unresolved == system-ish; IgnoreQueryFilters covers all rows.
        var options = NpgsqlOptions();
        using var db = new AppDbContext(options, ctx);
        var cleanup = new HrReportExportCleanupService(db, NullLogger<HrReportExportCleanupService>.Instance);

        var result = await cleanup.ExpireOverdueExportsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1); // only the overdue one.

        var expiredCount = await db.HrReportExports.IgnoreQueryFilters()
            .CountAsync(e => e.Status == HrReportExportStatus.Expired);
        expiredCount.Should().Be(1);
    }
}
