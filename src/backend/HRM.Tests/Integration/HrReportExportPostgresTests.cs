// ============================================================================
// US-RPT-004: HR report export — service integration tests on REAL POSTGRES (E3, Leg-3 parity).
//
// Exercises HrReportExportService over a real AppDbContext with the ITenantContext-driven global
// query filter, the real LocalReportExportStorage (writes to a temp path, which the download path reads back),
// and a controllable fake IHrReportService so row-count-driven routing can be tested without seeding thousands
// of employees. Covers:
//   - FR-5: < 1000 rows → rendered inline + Completed (no job enqueued).
//   - FR-5: >= 1000 rows → Queued + job enqueued (not rendered inline).
//   - FR-10: a 4th export with 3 already in progress → 429.
//   - AC-5: a download from another tenant / another user → null (controller → 404); cross-tenant isolation.
//   - BR-3: an expired export → a distinct Expired result (controller → 410).
//   (the BR-3 cleanup arm moved to HrReportExportCleanupPostgresTests — see PROVIDER note.)
//   - FR-9: an audit row ("HrReport.Export") is written on every initiation.
//
// PROVIDER: real PostgreSQL via Testcontainers (was EF InMemory until 2026-09-03, E3 slice 1). Export
// routing is driven by row counts, ordering and lifecycle state that InMemory — a LINQ-to-Objects provider
// with no SQL translation, no FK enforcement and no unique indexes — cannot faithfully reproduce. Every
// assertion is UNCHANGED from the InMemory suite; only the provider and the FK-valid seed rows differ.
// The BR-3 cleanup arm asserts a CROSS-TENANT count (IgnoreQueryFilters) so it cannot share a database with
// its siblings; it lives in HrReportExportCleanupPostgresTests with its own container, assertions intact.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Reports.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Integration;

[Trait("TC", "TC-RPT-004-PG")]
public sealed class HrReportExportPostgresTests : IClassFixture<PostgresContainerFixture>
{
    private readonly PostgresContainerFixture _pg;

    public HrReportExportPostgresTests(PostgresContainerFixture pg) => _pg = pg;

    private DbContextOptions<AppDbContext> NpgsqlOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_pg.ConnectionString, n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userA2 = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; set; }
        public string Email => "u@t.com";
        public Guid TenantId { get; set; }
        public Guid UserTenantId => Guid.NewGuid();
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => UserId != Guid.Empty;
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
    }

    /// <summary>A controllable report producer: returns a table with exactly <see cref="RowCount"/> rows.</summary>
    private sealed class FakeReportService : IHrReportService
    {
        public int RowCount { get; set; }
        public IReadOnlyList<HrReportDescriptorDto> ListReportTypes() => HrReportTypeKey.Catalog();

        public Task<Result<HrReportResult>> GenerateReportAsync(
            HrReportType reportType, HrReportQueryParams queryParams, bool refresh = false,
            CancellationToken cancellationToken = default)
        {
            var rows = Enumerable.Range(1, RowCount)
                .Select(i => new object?[] { $"EMP{i}", i })
                .Cast<object?[]>()
                .ToList();

            return Task.FromResult(Result<HrReportResult>.Success(new HrReportResult
            {
                Metadata = new HrReportMetadata
                {
                    Type = HrReportTypeKey.ToKey(reportType),
                    Title = HrReportTypeKey.Title(reportType),
                    GeneratedAt = DateTime.UtcNow,
                    AppliedFilters = new HrAppliedFilters(),
                },
                Table = new HrReportTable { Columns = ["Employee", "Value"], Rows = rows },
            }));
        }
    }

    private sealed class RecordingScheduler : IHrReportExportJobScheduler
    {
        public List<(Guid TenantId, Guid ExportId)> Enqueued { get; } = [];
        public void EnqueueGeneration(Guid tenantId, Guid exportId) => Enqueued.Add((tenantId, exportId));
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = NpgsqlOptions();
        return new AppDbContext(options, ctx);
    }

    private (AppDbContext Db, HrReportExportService Svc, FakeReportService Report, RecordingScheduler Scheduler) Scope(
        Guid tenantId, Guid userId, int rowCount)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = NpgsqlOptions();
        var db = new AppDbContext(options, ctx);
        var report = new FakeReportService { RowCount = rowCount };
        var scheduler = new RecordingScheduler();
        var storage = new LocalReportExportStorage(NullLogger<LocalReportExportStorage>.Instance);
        var currentUser = new FakeCurrentUser { UserId = userId, TenantId = tenantId };
        var svc = new HrReportExportService(
            db, ctx, currentUser, report, storage, NullLogger<HrReportExportService>.Instance,
            notifications: null, scheduler: scheduler);
        return (db, svc, report, scheduler);
    }

    // ── FR-5: sync routing (< 1000 rows → Completed inline) ──────────────────

    [Fact]
    public async Task Initiate_SmallReport_RendersInline_AndCompletes()
    {
        var (db, svc, _, scheduler) = Scope(_tenantA, _userA, rowCount: 50);
        using (db)
        {
            var result = await svc.InitiateAsync("headcount", new HrReportQueryParams(), "csv", includeCharts: true);

            result.IsSuccess.Should().BeTrue();
            result.Value!.Status.Should().Be("Completed");
            result.Value.RowCount.Should().Be(50);
            result.Value.Format.Should().Be("csv");

            scheduler.Enqueued.Should().BeEmpty(); // sync path never enqueues a job.

            var row = await db.HrReportExports.SingleAsync(e => e.Id == result.Value.ExportId);
            row.Status.Should().Be(HrReportExportStatus.Completed);
            row.ExpiresAt.Should().NotBeNull();
            row.FilePath.Should().NotBeNullOrEmpty();
            row.FileSizeBytes.Should().BeGreaterThan(0);
        }
    }

    // ── FR-5: async routing (>= 1000 rows → Queued + job enqueued) ───────────

    [Fact]
    public async Task Initiate_LargeReport_QueuesAndEnqueuesJob()
    {
        var (db, svc, _, scheduler) = Scope(_tenantA, _userA, rowCount: 1000);
        using (db)
        {
            var result = await svc.InitiateAsync("turnover", new HrReportQueryParams(), "xlsx", includeCharts: false);

            result.IsSuccess.Should().BeTrue();
            result.Value!.Status.Should().Be("Queued");
            result.Value.RowCount.Should().Be(1000);

            scheduler.Enqueued.Should().ContainSingle()
                .Which.Should().Be((_tenantA, result.Value.ExportId));

            var row = await db.HrReportExports.SingleAsync(e => e.Id == result.Value.ExportId);
            row.Status.Should().Be(HrReportExportStatus.Queued);
            row.FilePath.Should().BeNull(); // not rendered inline.
        }
    }

    [Fact]
    public async Task GenerateAsync_CompletesAQueuedExport()
    {
        var (db, svc, _, _) = Scope(_tenantA, _userA, rowCount: 1500);
        using (db)
        {
            var init = await svc.InitiateAsync("headcount", new HrReportQueryParams(), "pdf", includeCharts: true);
            init.Value!.Status.Should().Be("Queued");

            var gen = await svc.GenerateAsync(init.Value.ExportId);
            gen.IsSuccess.Should().BeTrue();

            var row = await db.HrReportExports.SingleAsync(e => e.Id == init.Value.ExportId);
            row.Status.Should().Be(HrReportExportStatus.Completed);
            row.FilePath.Should().NotBeNullOrEmpty();
            row.ExpiresAt.Should().NotBeNull();
        }
    }

    // ── FR-10: concurrency limit (4th with 3 in progress → 429) ──────────────

    [Fact]
    public async Task Initiate_FourthExport_WithThreeInProgress_Returns429()
    {
        // Seed 3 in-progress (Queued/Processing) exports for the user directly.
        using (var seed = Db(_tenantA))
        {
            for (int i = 0; i < 3; i++)
                seed.HrReportExports.Add(new HrReportExport
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantA,
                    RequestedByUserId = _userA,
                    ReportType = "headcount",
                    Format = HrReportExportFormat.Csv,
                    FiltersJson = "{}",
                    Status = i == 0 ? HrReportExportStatus.Processing : HrReportExportStatus.Queued,
                    RequestedAt = DateTime.UtcNow,
                });
            await seed.SaveChangesAsync();
        }

        var (db, svc, _, _) = Scope(_tenantA, _userA, rowCount: 10);
        using (db)
        {
            var result = await svc.InitiateAsync("headcount", new HrReportQueryParams(), "csv", includeCharts: true);

            result.IsFailure.Should().BeTrue();
            result.StatusCode.Should().Be(429);
            result.ErrorCode.Should().Be("export_limit_reached");
        }
    }

    [Fact]
    public async Task Initiate_FourthExport_ForADifferentUser_IsAllowed()
    {
        // user A has 3 in progress; user A2 (same tenant) is unaffected by A's limit.
        using (var seed = Db(_tenantA))
        {
            for (int i = 0; i < 3; i++)
                seed.HrReportExports.Add(new HrReportExport
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, RequestedByUserId = _userA,
                    ReportType = "headcount", Format = HrReportExportFormat.Csv, FiltersJson = "{}",
                    Status = HrReportExportStatus.Queued, RequestedAt = DateTime.UtcNow,
                });
            await seed.SaveChangesAsync();
        }

        var (db, svc, _, _) = Scope(_tenantA, _userA2, rowCount: 10);
        using (db)
        {
            var result = await svc.InitiateAsync("headcount", new HrReportQueryParams(), "csv", includeCharts: true);
            result.IsSuccess.Should().BeTrue();
        }
    }

    // ── AC-5: download tenant + owner isolation ──────────────────────────────

    [Fact]
    public async Task Download_FromAnotherTenant_ReturnsNull()
    {
        Guid exportId;
        var (dbA, svcA, _, _) = Scope(_tenantA, _userA, rowCount: 20);
        using (dbA)
        {
            var init = await svcA.InitiateAsync("headcount", new HrReportQueryParams(), "csv", includeCharts: true);
            exportId = init.Value!.ExportId;
        }

        // Tenant B user attempts the download → the global query filter hides A's row → null (controller → 404).
        var (dbB, svcB, _, _) = Scope(_tenantB, _userB, rowCount: 0);
        using (dbB)
        {
            var result = await svcB.GetForDownloadAsync(exportId);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeNull();
        }
    }

    [Fact]
    public async Task Download_ByAnotherUserInSameTenant_ReturnsNull()
    {
        Guid exportId;
        var (dbA, svcA, _, _) = Scope(_tenantA, _userA, rowCount: 20);
        using (dbA)
        {
            var init = await svcA.InitiateAsync("headcount", new HrReportQueryParams(), "csv", includeCharts: true);
            exportId = init.Value!.ExportId;
        }

        var (db2, svc2, _, _) = Scope(_tenantA, _userA2, rowCount: 0);
        using (db2)
        {
            var result = await svc2.GetForDownloadAsync(exportId);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().BeNull(); // not the owner.
        }
    }

    [Fact]
    public async Task Download_OwnCompletedExport_ReturnsFileBytes()
    {
        var (db, svc, _, _) = Scope(_tenantA, _userA, rowCount: 20);
        using (db)
        {
            var init = await svc.InitiateAsync("headcount", new HrReportQueryParams(), "csv", includeCharts: true);

            var result = await svc.GetForDownloadAsync(init.Value!.ExportId);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Expired.Should().BeFalse();
            result.Value.Content.Length.Should().BeGreaterThan(0);
            result.Value.ContentType.Should().Be("text/csv");
        }
    }

    // ── BR-3: expired download → distinct Expired result (→ 410) ─────────────

    [Fact]
    public async Task Download_ExpiredExport_ReturnsExpiredResult()
    {
        Guid exportId = BaseEntity.NewUuidV7();
        using (var seed = Db(_tenantA))
        {
            seed.HrReportExports.Add(new HrReportExport
            {
                Id = exportId, TenantId = _tenantA, RequestedByUserId = _userA,
                ReportType = "headcount", Format = HrReportExportFormat.Csv, FiltersJson = "{}",
                Status = HrReportExportStatus.Completed, RowCount = 5, FileSizeBytes = 100,
                FilePath = "/tmp/does-not-matter.csv",
                RequestedAt = DateTime.UtcNow.AddDays(-8),
                CompletedAt = DateTime.UtcNow.AddDays(-8),
                ExpiresAt = DateTime.UtcNow.AddDays(-1), // already past the 7-day window.
            });
            await seed.SaveChangesAsync();
        }

        var (db, svc, _, _) = Scope(_tenantA, _userA, rowCount: 0);
        using (db)
        {
            var result = await svc.GetForDownloadAsync(exportId);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Expired.Should().BeTrue();
        }
    }

    // ── FR-9: audit row written on every export ──────────────────────────────

    [Fact]
    public async Task Initiate_WritesAuditRow_WithReportTypeFormatRowCountAndActor()
    {
        var (db, svc, _, _) = Scope(_tenantA, _userA, rowCount: 30);
        using (db)
        {
            var init = await svc.InitiateAsync("turnover", new HrReportQueryParams(), "pdf", includeCharts: true);

            var audit = await db.AuditLogs
                .Where(a => a.Action == "HrReport.Export" && a.ResourceId == init.Value!.ExportId.ToString())
                .SingleAsync();

            audit.TenantId.Should().Be(_tenantA);
            audit.UserId.Should().Be(_userA);
            audit.ResourceType.Should().Be("HrReportExport");
            audit.Detail.Should().Contain("reportType=turnover");
            audit.Detail.Should().Contain("format=Pdf");
            audit.Detail.Should().Contain("rows=30");
        }
    }

    [Fact]
    public async Task Download_WritesDownloadAuditRow()
    {
        var (db, svc, _, _) = Scope(_tenantA, _userA, rowCount: 15);
        using (db)
        {
            var init = await svc.InitiateAsync("headcount", new HrReportQueryParams(), "csv", includeCharts: true);
            await svc.GetForDownloadAsync(init.Value!.ExportId);

            var audit = await db.AuditLogs
                .Where(a => a.Action == "HrReport.ExportDownloaded"
                    && a.ResourceId == init.Value.ExportId.ToString())
                .SingleAsync();
            audit.UserId.Should().Be(_userA);
        }
    }

    // ── Validation: unknown type / format → 400 ──────────────────────────────

    [Fact]
    public async Task Initiate_UnknownReportType_Returns400()
    {
        var (db, svc, _, _) = Scope(_tenantA, _userA, rowCount: 10);
        using (db)
        {
            var result = await svc.InitiateAsync("not-a-report", new HrReportQueryParams(), "csv", includeCharts: true);
            result.IsFailure.Should().BeTrue();
            result.StatusCode.Should().Be(400);
            result.ErrorCode.Should().Be("unknown_report_type");
        }
    }

    [Fact]
    public async Task Initiate_UnsupportedFormat_Returns400()
    {
        var (db, svc, _, _) = Scope(_tenantA, _userA, rowCount: 10);
        using (db)
        {
            var result = await svc.InitiateAsync("headcount", new HrReportQueryParams(), "docx", includeCharts: true);
            result.IsFailure.Should().BeTrue();
            result.StatusCode.Should().Be(400);
            result.ErrorCode.Should().Be("unsupported_format");
        }
    }

}
