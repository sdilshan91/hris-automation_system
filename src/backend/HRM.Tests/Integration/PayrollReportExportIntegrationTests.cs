// ============================================================================
// ISSUE-178 PR2: async large payroll-report export — service integration tests (InMemory).
//
// Exercises PayrollReportExportService over a real AppDbContext (InMemory) with the ITenantContext-driven global
// query filter, the real LocalReportExportStorage (writes to a temp path, which the download path reads back),
// and a controllable fake IPayrollReportService so row-count-driven routing can be tested without seeding
// thousands of payroll slips. Covers:
//   - < 1000 rows → rendered inline + Completed (no job enqueued).
//   - >= 1000 rows → Queued + job enqueued (not rendered inline).
//   - GenerateAsync completes a queued export (sets FilePath/ExpiresAt) + dispatches ReportExportReady.
//   - a 4th export with 3 already in progress → 429.
//   - a download from another tenant / another user → null (controller → 404); cross-tenant isolation.
//   - an expired export → a distinct Expired result (controller → 410).
//   - an audit row ("PayrollReport.Export") is written on every initiation.
//   - the 7-day retention cleanup expires overdue completed exports.
//
// A deliberate 1:1 clone of HrReportExportIntegrationTests, adapted for payroll.
//
// PROVIDER: InMemory — same rationale as the other integration tests (the verify gate runs `dotnet test` with
// no PostgreSQL / Docker). The Postgres/Testcontainers suite applies the migration + RLS coverage guard.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Integration;

public sealed class PayrollReportExportIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
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

    /// <summary>A controllable payroll-report producer: a report with exactly <see cref="RowCount"/> rows, and a
    /// stub export that always renders a small non-empty CSV. Records that ExportReportAsync was invoked so tests
    /// can assert the async path never renders inline.</summary>
    private sealed class FakePayrollReportService : IPayrollReportService
    {
        public int RowCount { get; set; }
        public int ExportCallCount { get; private set; }

        public IReadOnlyList<PayrollReportDescriptorDto> ListReportTypes() => [];

        // US-PAY-009 AC-3: this fake exists to exercise the EXPORT routing, not the statement paths, so these
        // return a not-available failure rather than a fabricated success. A fake that pretended to produce a
        // PDF would make any future test asserting on one pass against nothing.
        public Task<Result<PayrollReportExportResult>> GetYearEndTaxStatementPdfAsync(
            Guid employeeId, int year, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PayrollReportExportResult>.Failure(
                "Not implemented by this fake.", 404, "statement_not_available"));

        public Task<Result<PayrollReportExportResult>> GetYearEndTaxStatementsBundleAsync(
            int year, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PayrollReportExportResult>.Failure(
                "Not implemented by this fake.", 404, "statements_not_available"));

        public Task<Result<PayrollReportExportResult>> GetMyYearEndTaxStatementPdfAsync(
            int year, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<PayrollReportExportResult>.Failure(
                "Not implemented by this fake.", 404, "statement_not_available"));

        public Task<Result<PayrollReportResult>> GenerateReportAsync(
            PayrollReportType reportType, PayrollReportQueryParams queryParams, CancellationToken cancellationToken = default)
        {
            var rows = Enumerable.Range(1, RowCount)
                .Select(i => new PayrollReportRow { Cells = [$"EMP{i}", i.ToString()] })
                .ToList();

            return Task.FromResult(Result<PayrollReportResult>.Success(new PayrollReportResult
            {
                ReportType = reportType.ToString(),
                Title = $"{reportType} — Test",
                PayMonth = 5,
                PayYear = 2026,
                Columns = ["Employee", "Value"],
                Rows = rows,
                TotalCount = rows.Count,
            }));
        }

        public Task<Result<PayrollAnalyticsResult>> GetAnalyticsAsync(
            PayrollAnalyticsChartType chartType, PayrollReportQueryParams queryParams, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<PayrollAnalyticsResult>.Success(new PayrollAnalyticsResult()));

        public Task<Result<BankAdvicePreviewDto>> GetBankAdvicePreviewAsync(
            PayrollReportQueryParams queryParams, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<BankAdvicePreviewDto>.Success(new BankAdvicePreviewDto()));

        public Task<Result<BankAdvicePreviewDto>> RevealBankAdviceAsync(
            PayrollReportQueryParams queryParams, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<BankAdvicePreviewDto>.Success(new BankAdvicePreviewDto()));

        public Task<Result<PayrollReportExportResult>> ExportReportAsync(
            PayrollReportType reportType, PayrollExportFormat format, PayrollReportQueryParams queryParams,
            CancellationToken cancellationToken = default)
        {
            ExportCallCount++;
            var ext = format switch
            {
                PayrollExportFormat.Xlsx => "xlsx",
                PayrollExportFormat.Pdf => "pdf",
                _ => "csv",
            };
            var contentType = format switch
            {
                PayrollExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                PayrollExportFormat.Pdf => "application/pdf",
                _ => "text/csv",
            };
            return Task.FromResult(Result<PayrollReportExportResult>.Success(new PayrollReportExportResult
            {
                FileContent = "Employee,Value\r\nEMP1,1\r\n"u8.ToArray(),
                FileName = $"payroll-{ToKebab(reportType.ToString())}-2026-05.{ext}",
                ContentType = contentType,
            }));
        }

        private static string ToKebab(string s) => string.Concat(
            s.Select((c, i) => char.IsUpper(c) && i > 0 ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }

    private sealed class RecordingScheduler : IPayrollReportExportJobScheduler
    {
        public List<(Guid TenantId, Guid ExportId)> Enqueued { get; } = [];
        public void EnqueueGeneration(Guid tenantId, Guid exportId) => Enqueued.Add((tenantId, exportId));
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public List<(Guid TenantId, Guid User, string Type, string ResourceId)> Sent { get; } = [];

        public Task<Guid> CreateAndDispatchAsync(
            Guid tenantId, Guid recipientUserId, string type, string title, string message,
            string? resourceType = null, string? resourceId = null, CancellationToken cancellationToken = default)
        {
            Sent.Add((tenantId, recipientUserId, type, resourceId ?? ""));
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private (AppDbContext Db, PayrollReportExportService Svc, FakePayrollReportService Report,
        RecordingScheduler Scheduler, RecordingNotifications Notifications) Scope(
        Guid tenantId, Guid userId, int rowCount)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        var db = new AppDbContext(options, ctx);
        var report = new FakePayrollReportService { RowCount = rowCount };
        var scheduler = new RecordingScheduler();
        var notifications = new RecordingNotifications();
        var storage = new LocalReportExportStorage(NullLogger<LocalReportExportStorage>.Instance);
        var currentUser = new FakeCurrentUser { UserId = userId, TenantId = tenantId };
        var svc = new PayrollReportExportService(
            db, ctx, currentUser, report, storage, NullLogger<PayrollReportExportService>.Instance,
            notifications: notifications, scheduler: scheduler);
        return (db, svc, report, scheduler, notifications);
    }

    // ── sync routing (< 1000 rows → Completed inline) ────────────────────────

    [Fact]
    public async Task Initiate_SmallReport_RendersInline_AndCompletes()
    {
        var (db, svc, report, scheduler, _) = Scope(_tenantA, _userA, rowCount: 50);
        using (db)
        {
            var result = await svc.InitiateAsync("PayrollSummary", new PayrollReportQueryParams(), "csv");

            result.IsSuccess.Should().BeTrue();
            result.Value!.Status.Should().Be("Completed");
            result.Value.RowCount.Should().Be(50);
            result.Value.Format.Should().Be("csv");

            scheduler.Enqueued.Should().BeEmpty();      // sync path never enqueues a job.
            report.ExportCallCount.Should().Be(1);      // rendered inline via the shipped export path.

            var row = await db.PayrollReportExports.SingleAsync(e => e.Id == result.Value.ExportId);
            row.Status.Should().Be(PayrollReportExportStatus.Completed);
            row.ExpiresAt.Should().NotBeNull();
            row.FilePath.Should().NotBeNullOrEmpty();
            row.FileSizeBytes.Should().BeGreaterThan(0);
        }
    }

    // ── async routing (>= 1000 rows → Queued + job enqueued) ─────────────────

    [Fact]
    public async Task Initiate_LargeReport_QueuesAndEnqueuesJob()
    {
        var (db, svc, report, scheduler, _) = Scope(_tenantA, _userA, rowCount: 1000);
        using (db)
        {
            var result = await svc.InitiateAsync("EmployeeRegister", new PayrollReportQueryParams(), "xlsx");

            result.IsSuccess.Should().BeTrue();
            result.Value!.Status.Should().Be("Queued");
            result.Value.RowCount.Should().Be(1000);

            scheduler.Enqueued.Should().ContainSingle()
                .Which.Should().Be((_tenantA, result.Value.ExportId));
            report.ExportCallCount.Should().Be(0);      // NOT rendered inline on the async path.

            var row = await db.PayrollReportExports.SingleAsync(e => e.Id == result.Value.ExportId);
            row.Status.Should().Be(PayrollReportExportStatus.Queued);
            row.FilePath.Should().BeNull();
        }
    }

    [Fact]
    public async Task GenerateAsync_CompletesAQueuedExport_AndNotifies()
    {
        var (db, svc, _, _, notifications) = Scope(_tenantA, _userA, rowCount: 1500);
        using (db)
        {
            var init = await svc.InitiateAsync("PayrollSummary", new PayrollReportQueryParams(), "pdf");
            init.Value!.Status.Should().Be("Queued");

            var gen = await svc.GenerateAsync(init.Value.ExportId);
            gen.IsSuccess.Should().BeTrue();

            var row = await db.PayrollReportExports.SingleAsync(e => e.Id == init.Value.ExportId);
            row.Status.Should().Be(PayrollReportExportStatus.Completed);
            row.FilePath.Should().NotBeNullOrEmpty();
            row.ExpiresAt.Should().NotBeNull();

            // async-complete notification dispatched to the requester.
            notifications.Sent.Should().ContainSingle()
                .Which.Should().Be((_tenantA, _userA, "ReportExportReady", init.Value.ExportId.ToString()));
        }
    }

    // ── concurrency limit (4th with 3 in progress → 429) ─────────────────────

    [Fact]
    public async Task Initiate_FourthExport_WithThreeInProgress_Returns429()
    {
        using (var seed = Db(_tenantA))
        {
            for (int i = 0; i < 3; i++)
                seed.PayrollReportExports.Add(new PayrollReportExport
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantA,
                    RequestedByUserId = _userA,
                    ReportType = "PayrollSummary",
                    Format = PayrollReportExportFormat.Csv,
                    FiltersJson = "{}",
                    Status = i == 0 ? PayrollReportExportStatus.Processing : PayrollReportExportStatus.Queued,
                    RequestedAt = DateTime.UtcNow,
                });
            await seed.SaveChangesAsync();
        }

        var (db, svc, _, _, _) = Scope(_tenantA, _userA, rowCount: 10);
        using (db)
        {
            var result = await svc.InitiateAsync("PayrollSummary", new PayrollReportQueryParams(), "csv");

            result.IsFailure.Should().BeTrue();
            result.StatusCode.Should().Be(429);
            result.ErrorCode.Should().Be("export_limit_reached");
        }
    }

    [Fact]
    public async Task Initiate_FourthExport_ForADifferentUser_IsAllowed()
    {
        using (var seed = Db(_tenantA))
        {
            for (int i = 0; i < 3; i++)
                seed.PayrollReportExports.Add(new PayrollReportExport
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, RequestedByUserId = _userA,
                    ReportType = "PayrollSummary", Format = PayrollReportExportFormat.Csv, FiltersJson = "{}",
                    Status = PayrollReportExportStatus.Queued, RequestedAt = DateTime.UtcNow,
                });
            await seed.SaveChangesAsync();
        }

        var (db, svc, _, _, _) = Scope(_tenantA, _userA2, rowCount: 10);
        using (db)
        {
            var result = await svc.InitiateAsync("PayrollSummary", new PayrollReportQueryParams(), "csv");
            result.IsSuccess.Should().BeTrue();
        }
    }

    // ── download tenant + owner isolation ────────────────────────────────────

    [Fact]
    public async Task Download_FromAnotherTenant_ReturnsNull()
    {
        Guid exportId;
        var (dbA, svcA, _, _, _) = Scope(_tenantA, _userA, rowCount: 20);
        using (dbA)
        {
            var init = await svcA.InitiateAsync("PayrollSummary", new PayrollReportQueryParams(), "csv");
            exportId = init.Value!.ExportId;
        }

        var (dbB, svcB, _, _, _) = Scope(_tenantB, _userB, rowCount: 0);
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
        var (dbA, svcA, _, _, _) = Scope(_tenantA, _userA, rowCount: 20);
        using (dbA)
        {
            var init = await svcA.InitiateAsync("PayrollSummary", new PayrollReportQueryParams(), "csv");
            exportId = init.Value!.ExportId;
        }

        var (db2, svc2, _, _, _) = Scope(_tenantA, _userA2, rowCount: 0);
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
        var (db, svc, _, _, _) = Scope(_tenantA, _userA, rowCount: 20);
        using (db)
        {
            var init = await svc.InitiateAsync("PayrollSummary", new PayrollReportQueryParams(), "csv");

            var result = await svc.GetForDownloadAsync(init.Value!.ExportId);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Expired.Should().BeFalse();
            result.Value.Content.Length.Should().BeGreaterThan(0);
            result.Value.ContentType.Should().Be("text/csv");
        }
    }

    // ── expired download → distinct Expired result (→ 410) ───────────────────

    [Fact]
    public async Task Download_ExpiredExport_ReturnsExpiredResult()
    {
        Guid exportId = BaseEntity.NewUuidV7();
        using (var seed = Db(_tenantA))
        {
            seed.PayrollReportExports.Add(new PayrollReportExport
            {
                Id = exportId, TenantId = _tenantA, RequestedByUserId = _userA,
                ReportType = "PayrollSummary", Format = PayrollReportExportFormat.Csv, FiltersJson = "{}",
                Status = PayrollReportExportStatus.Completed, RowCount = 5, FileSizeBytes = 100,
                FilePath = "/tmp/does-not-matter.csv",
                RequestedAt = DateTime.UtcNow.AddDays(-8),
                CompletedAt = DateTime.UtcNow.AddDays(-8),
                ExpiresAt = DateTime.UtcNow.AddDays(-1), // already past the 7-day window.
            });
            await seed.SaveChangesAsync();
        }

        var (db, svc, _, _, _) = Scope(_tenantA, _userA, rowCount: 0);
        using (db)
        {
            var result = await svc.GetForDownloadAsync(exportId);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Expired.Should().BeTrue();
        }
    }

    // Tenant-filter isolation for the download (independent of the owner check) is proven on real Postgres under
    // RLS by RlsIsolationPostgresTests — the InMemory provider can't isolate the query filter per-context within
    // one test process (shared model cache), so it's a P7 Postgres follow-up, not asserted here.

    // GenerateAsync on an ALREADY-Completed export is IDEMPOTENT (early-return) — it must NOT re-render the report
    // or re-dispatch the ReportExportReady notification (guards a duplicate-work regression on job retries).
    [Fact]
    public async Task Generate_AlreadyCompletedExport_IsIdempotent_NoReRender()
    {
        var (db, svc, report, _, notifications) = Scope(_tenantA, _userA, rowCount: 30);
        using (db)
        {
            // Small report → rendered inline + Completed (one ExportReportAsync call).
            var init = await svc.InitiateAsync("PayrollSummary", new PayrollReportQueryParams(), "csv");
            init.Value!.Status.Should().Be("Completed");
            report.ExportCallCount.Should().Be(1);
            var notifBefore = notifications.Sent.Count;

            // Re-driving GenerateAsync on the already-Completed export is a no-op.
            var again = await svc.GenerateAsync(init.Value.ExportId);
            again.IsSuccess.Should().BeTrue();

            report.ExportCallCount.Should().Be(1);                    // NOT re-rendered.
            notifications.Sent.Count.Should().Be(notifBefore);       // no duplicate ReportExportReady.
            (await db.PayrollReportExports.FirstAsync(e => e.Id == init.Value.ExportId)).Status
                .Should().Be(PayrollReportExportStatus.Completed);
        }
    }

    // The Status==Expired branch is independent of ExpiresAt: seed Status=Expired with a FUTURE ExpiresAt so only
    // the status check can trigger the 410 (complements the ExpiresAt<=now arm above).
    [Fact]
    public async Task Download_StatusExpired_FutureExpiresAt_ReturnsExpiredResult()
    {
        Guid exportId = BaseEntity.NewUuidV7();
        using (var seed = Db(_tenantA))
        {
            seed.PayrollReportExports.Add(new PayrollReportExport
            {
                Id = exportId, TenantId = _tenantA, RequestedByUserId = _userA,
                ReportType = "PayrollSummary", Format = PayrollReportExportFormat.Csv, FiltersJson = "{}",
                Status = PayrollReportExportStatus.Expired, RowCount = 5, FileSizeBytes = 100,
                FilePath = "/tmp/does-not-matter.csv",
                RequestedAt = DateTime.UtcNow.AddDays(-2),
                CompletedAt = DateTime.UtcNow.AddDays(-2),
                ExpiresAt = DateTime.UtcNow.AddDays(5), // NOT past — only Status=Expired should trigger.
            });
            await seed.SaveChangesAsync();
        }

        var (db, svc, _, _, _) = Scope(_tenantA, _userA, rowCount: 0);
        using (db)
        {
            var result = await svc.GetForDownloadAsync(exportId);
            result.IsSuccess.Should().BeTrue();
            result.Value.Should().NotBeNull();
            result.Value!.Expired.Should().BeTrue();
        }
    }

    // ── audit row written on every export ────────────────────────────────────

    [Fact]
    public async Task Initiate_WritesAuditRow_WithReportTypeFormatRowCountAndActor()
    {
        var (db, svc, _, _, _) = Scope(_tenantA, _userA, rowCount: 30);
        using (db)
        {
            var init = await svc.InitiateAsync("EmployeeRegister", new PayrollReportQueryParams(), "pdf");

            var audit = await db.AuditLogs
                .Where(a => a.Action == "PayrollReport.Export" && a.ResourceId == init.Value!.ExportId.ToString())
                .SingleAsync();

            audit.TenantId.Should().Be(_tenantA);
            audit.UserId.Should().Be(_userA);
            audit.ResourceType.Should().Be("PayrollReportExport");
            audit.Detail.Should().Contain("reportType=EmployeeRegister");
            audit.Detail.Should().Contain("format=Pdf");
            audit.Detail.Should().Contain("rows=30");
        }
    }

    [Fact]
    public async Task Download_WritesDownloadAuditRow()
    {
        var (db, svc, _, _, _) = Scope(_tenantA, _userA, rowCount: 15);
        using (db)
        {
            var init = await svc.InitiateAsync("PayrollSummary", new PayrollReportQueryParams(), "csv");
            await svc.GetForDownloadAsync(init.Value!.ExportId);

            var audit = await db.AuditLogs
                .Where(a => a.Action == "PayrollReport.ExportDownloaded"
                    && a.ResourceId == init.Value.ExportId.ToString())
                .SingleAsync();
            audit.UserId.Should().Be(_userA);
        }
    }

    // ── validation: unknown type / format → 400 ──────────────────────────────

    [Fact]
    public async Task Initiate_UnknownReportType_Returns400()
    {
        var (db, svc, _, _, _) = Scope(_tenantA, _userA, rowCount: 10);
        using (db)
        {
            var result = await svc.InitiateAsync("NotAReport", new PayrollReportQueryParams(), "csv");
            result.IsFailure.Should().BeTrue();
            result.StatusCode.Should().Be(400);
            result.ErrorCode.Should().Be("unknown_report_type");
        }
    }

    [Fact]
    public async Task Initiate_UnsupportedFormat_Returns400()
    {
        var (db, svc, _, _, _) = Scope(_tenantA, _userA, rowCount: 10);
        using (db)
        {
            var result = await svc.InitiateAsync("PayrollSummary", new PayrollReportQueryParams(), "docx");
            result.IsFailure.Should().BeTrue();
            result.StatusCode.Should().Be(400);
            result.ErrorCode.Should().Be("unsupported_format");
        }
    }

    // ── TAX-4: the new YearEndTaxStatement type routes through the REAL report service + the async export
    //    pipeline. A small tenant renders inline + Completes; the export row count == the employee count. ──

    [Fact]
    public async Task Initiate_YearEndTaxStatement_GeneratesInline_WithRowCountMatchingEmployees()
    {
        // Seed a small LK tenant: an income-tax FY rule + a branch + two employees with finalized tax slips.
        using (var seed = Db(_tenantA))
        {
            var lkLocId = BaseEntity.NewUuidV7();
            seed.Locations.Add(new Location
            {
                Id = lkLocId, TenantId = _tenantA, Name = "Colombo", TimeZone = "Asia/Colombo",
                CountryCode = "LK", IsActive = true, IsDeleted = false,
            });
            seed.StatutoryRules.Add(new StatutoryRule
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, RuleType = StatutoryRuleType.IncomeTax,
                RuleName = "PAYE", CountryCode = "LK", FiscalYear = "2026-2027",
                EffectiveFrom = new DateOnly(2026, 4, 1), EffectiveTo = new DateOnly(2027, 3, 31), IsActive = true,
            });

            var runId = BaseEntity.NewUuidV7();
            seed.PayrollRuns.Add(new PayrollRun
            {
                Id = runId, TenantId = _tenantA, PayMonth = 12, PayYear = 2026,
                Status = PayrollRunStatus.Finalized, InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
            });

            foreach (var no in new[] { "LK1", "LK2" })
            {
                var empId = BaseEntity.NewUuidV7();
                seed.Employees.Add(new Employee
                {
                    Id = empId, TenantId = _tenantA, EmployeeNo = no, FirstName = no, LastName = "X",
                    Email = $"{no}@t.com", DateOfJoining = new DateTime(2020, 1, 1),
                    EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
                    DepartmentId = BaseEntity.NewUuidV7(), JobTitleId = BaseEntity.NewUuidV7(), LocationId = lkLocId,
                });
                seed.PayrollSlips.Add(new PayrollSlip
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, PayrollRunId = runId, EmployeeId = empId,
                    GrossEarnings = 100_000m, TotalDeductions = 5_000m, NetSalary = 95_000m,
                    TaxableIncome = 100_000m, IncomeTaxWithheld = 5_000m,
                    WorkingDays = 22, PaidDays = 22, LopDays = 0, PayMonth = 6, PayYear = 2026,
                });
            }
            await seed.SaveChangesAsync();
        }

        // Real report service (not the fake) behind a real export service, so the async pipeline actually
        // generates the year-end statement.
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        var currentUser = new FakeCurrentUser { UserId = _userA, TenantId = _tenantA };
        var audit = new PayrollAuditLogger(db, ctx, currentUser, NullLogger<PayrollAuditLogger>.Instance);
        var report = new PayrollReportService(db, ctx, audit, NullLogger<PayrollReportService>.Instance);
        var storage = new LocalReportExportStorage(NullLogger<LocalReportExportStorage>.Instance);
        var svc = new PayrollReportExportService(
            db, ctx, currentUser, report, storage, NullLogger<PayrollReportExportService>.Instance);

        var init = await svc.InitiateAsync(
            "YearEndTaxStatement", new PayrollReportQueryParams { PayMonth = 12, PayYear = 2026 }, "csv");

        init.IsSuccess.Should().BeTrue(init.Error);
        init.Value!.Status.Should().Be("Completed"); // small (2 rows) → inline, no Hangfire job.
        init.Value.RowCount.Should().Be(2);           // one row per employee.

        var row = await db.PayrollReportExports.SingleAsync(e => e.Id == init.Value.ExportId);
        row.Status.Should().Be(PayrollReportExportStatus.Completed);
        row.RowCount.Should().Be(2);
        row.FilePath.Should().NotBeNullOrEmpty();
    }

    // ── FiltersJson round-trips the PR1 salary-structure/date-range filters ───

    [Fact]
    public async Task Initiate_PersistsFullQueryParams_IncludingPr1Filters()
    {
        var structureId = Guid.NewGuid();
        var filters = new PayrollReportQueryParams
        {
            SalaryStructureId = structureId,
            DateFrom = new DateOnly(2026, 1, 1),
            DateTo = new DateOnly(2026, 5, 31),
        };

        var (db, svc, _, _, _) = Scope(_tenantA, _userA, rowCount: 5);
        using (db)
        {
            var init = await svc.InitiateAsync("PayrollSummary", filters, "csv");

            var row = await db.PayrollReportExports.SingleAsync(e => e.Id == init.Value!.ExportId);
            row.FiltersJson.Should().Contain(structureId.ToString());
            row.FiltersJson.Should().Contain("2026-01-01");
            row.FiltersJson.Should().Contain("2026-05-31");
        }
    }

    // ── retention cleanup: expires overdue completed exports ─────────────────

    [Fact]
    public async Task CleanupService_ExpiresOverdueCompletedExports()
    {
        using (var seed = Db(_tenantA))
        {
            seed.PayrollReportExports.Add(new PayrollReportExport
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, RequestedByUserId = _userA,
                ReportType = "PayrollSummary", Format = PayrollReportExportFormat.Csv, FiltersJson = "{}",
                Status = PayrollReportExportStatus.Completed, RowCount = 5, FileSizeBytes = 100,
                FilePath = null,
                RequestedAt = DateTime.UtcNow.AddDays(-8), CompletedAt = DateTime.UtcNow.AddDays(-8),
                ExpiresAt = DateTime.UtcNow.AddDays(-1), // overdue
            });
            seed.PayrollReportExports.Add(new PayrollReportExport
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, RequestedByUserId = _userA,
                ReportType = "PayrollSummary", Format = PayrollReportExportFormat.Csv, FiltersJson = "{}",
                Status = PayrollReportExportStatus.Completed, RowCount = 5, FileSizeBytes = 100,
                FilePath = null,
                RequestedAt = DateTime.UtcNow, CompletedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(6), // still fresh
            });
            await seed.SaveChangesAsync();
        }

        var ctx = new MutableTenantContext(); // unresolved == system-ish; IgnoreQueryFilters covers all rows.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        var cleanup = new PayrollReportExportCleanupService(db, NullLogger<PayrollReportExportCleanupService>.Instance);

        var result = await cleanup.ExpireOverdueExportsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1); // only the overdue one.

        var expiredCount = await db.PayrollReportExports.IgnoreQueryFilters()
            .CountAsync(e => e.Status == PayrollReportExportStatus.Expired);
        expiredCount.Should().Be(1);
    }
}
