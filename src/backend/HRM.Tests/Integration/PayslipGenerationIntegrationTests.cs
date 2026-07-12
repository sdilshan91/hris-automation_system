// ============================================================================
// US-PAY-004: Payslip PDF generation — integration tests.
//
// Exercises the full handler -> service/batch-renderer -> DbContext path through a real DI container
// (MediatR pipeline + ITenantContext-driven global query filters), covering:
//   - AC-1/FR-7: generate marks slips Pending + enqueues; the batch render updates each slip's PDF fields
//     (PdfStatus=Generated, PdfStoragePath, PdfGeneratedAt, PdfFileSizeBytes) and stores at the GUID-derived
//     path payroll/{runId}/{employeeId}.pdf (FR-5).
//   - BR-1: generate is rejected (400) for a run that is not ReviewPending/Approved/Finalized.
//   - AC-4: tenant A cannot download tenant B's payslip (cross-tenant => 404).
//   - AC-5: regenerate overwrites + updates the timestamp (and stores at the same path).
//   - FR-6: single download streams the PDF (BR-5 file name); bulk ZIP contains an entry per generated slip.
//
// PROVIDER: InMemory through the real composed pipeline — same rationale as the other Payroll integration
// tests (the verify gate runs `dotnet test` with no PostgreSQL / Docker). The Hangfire batch render is
// invoked directly (no live Hangfire server), per the story brief. A fake in-memory IFileStorage stands in
// for blob storage so QuestPDF renders to bytes with no real filesystem.
// ============================================================================

using System.IO.Compression;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration;

public sealed class PayslipGenerationIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly InMemoryFileStorage _storage = new();

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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; init; } = Guid.NewGuid();
        public string Email => "hr@t.com";
        public Guid TenantId { get; init; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
    }

    /// <summary>In-memory IFileStorage: stores bytes keyed by tenant + relative path (no filesystem).</summary>
    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Dictionary<(Guid, string), byte[]> Files { get; } = new();

        public async Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken ct = default)
        {
            using var ms = new MemoryStream();
            await content.CopyToAsync(ms, ct);
            Files[(tenantId, relativePath)] = ms.ToArray();
            return $"/{tenantId}/{relativePath}";
        }

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken ct = default)
            => Task.FromResult<Stream?>(Files.TryGetValue((tenantId, relativePath), out var bytes)
                ? new MemoryStream(bytes, writable: false) : null);

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null) => $"/{tenantId}/{relativePath}";
        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken ct = default)
        {
            Files.Remove((tenantId, relativePath));
            return Task.CompletedTask;
        }
    }

    private IServiceProvider Provider(Guid tenantId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId });
        services.AddSingleton<IFileStorage>(_storage);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<IPayslipGenerationService, PayslipGenerationService>();
        services.AddScoped<IPayslipBatchRenderer, PayslipBatchRenderer>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(GeneratePayslipsCommand).Assembly));
        return services.BuildServiceProvider();
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    /// <summary>Seeds a ReviewPending run with one slip (+ details) per employee. Returns the run id.</summary>
    private async Task<(Guid RunId, Guid EmployeeId)> SeedRunWithSlip(
        Guid tenantId, string employeeNo, int month = 5, int year = 2026, PayrollRunStatus status = PayrollRunStatus.ReviewPending)
    {
        using var db = Db(tenantId);

        var deptId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = "Engineering", Code = "ENG", IsActive = true });
        var jobId = BaseEntity.NewUuidV7();
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = tenantId, TitleName = "Engineer", IsActive = true });

        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, EmployeeNo = employeeNo, FirstName = "Jane", LastName = "Doe",
            Email = $"{employeeNo}@t.com", DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = deptId, JobTitleId = jobId,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = tenantId, PayMonth = month, PayYear = year, Status = status,
            InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow, TotalEmployees = 1, ProcessedEmployees = 1,
        });

        var slipId = BaseEntity.NewUuidV7();
        db.PayrollSlips.Add(new PayrollSlip
        {
            Id = slipId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = empId,
            GrossEarnings = 70_000m, TotalDeductions = 10_000m, NetSalary = 60_000m,
            WorkingDays = 22m, PaidDays = 22m, LopDays = 0m, PayMonth = month, PayYear = year,
        });
        db.PayrollSlipDetails.AddRange(
            new PayrollSlipDetail { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(), ComponentName = "Basic", ComponentType = nameof(SalaryComponentType.Earning), Amount = 50_000m },
            new PayrollSlipDetail { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(), ComponentName = "HRA", ComponentType = nameof(SalaryComponentType.Earning), Amount = 20_000m },
            new PayrollSlipDetail { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(), ComponentName = "PF", ComponentType = nameof(SalaryComponentType.Statutory), Amount = 6_000m },
            new PayrollSlipDetail { Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(), ComponentName = "Tax", ComponentType = nameof(SalaryComponentType.Deduction), Amount = 4_000m });

        await db.SaveChangesAsync();
        return (runId, empId);
    }

    // ── AC-1 / FR-7 / FR-5: generate updates slip pdf fields + stores at the GUID-derived path ──

    [Fact]
    public async Task Generate_ThenRender_UpdatesSlipPdfFields_AndStoresAtCorrectPath()
    {
        var (runId, empId) = await SeedRunWithSlip(_tenantA, "EMP-1");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        var accepted = await mediator.Send(new GeneratePayslipsCommand(runId));
        accepted.IsSuccess.Should().BeTrue();
        accepted.Value!.QueuedCount.Should().Be(1);
        accepted.Value.Regenerated.Should().BeFalse();

        var render = await provider.GetRequiredService<IPayslipBatchRenderer>().RenderRunAsync(runId);
        render.IsSuccess.Should().BeTrue();
        render.Value!.Generated.Should().Be(1);
        render.Value.Failed.Should().Be(0);

        using var db = Db(_tenantA);
        var slip = await db.PayrollSlips.FirstAsync(s => s.PayrollRunId == runId);
        slip.PdfStatus.Should().Be(PayslipPdfStatus.Generated);
        slip.PdfGeneratedAt.Should().NotBeNull();
        slip.PdfFileSizeBytes.Should().BeGreaterThan(0);
        slip.PdfStoragePath.Should().Be($"payroll/{runId:D}/{empId:D}.pdf");

        _storage.Files.Should().ContainKey((_tenantA, $"payroll/{runId:D}/{empId:D}.pdf"));
    }

    // ── US-PAY-012 / BUG-080: each generated PDF emits a system-actor audit ──

    [Fact]
    public async Task Render_EmitsPayslipPdfGeneratedAudit_PerSlip_AsSystemActor()
    {
        var (runId, empId) = await SeedRunWithSlip(_tenantA, "EMP-AUD");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Send(new GeneratePayslipsCommand(runId));
        var render = await provider.GetRequiredService<IPayslipBatchRenderer>().RenderRunAsync(runId);
        render.Value!.Generated.Should().Be(1);

        using var db = Db(_tenantA);
        var slipId = (await db.PayrollSlips.AsNoTracking().FirstAsync(s => s.PayrollRunId == runId)).Id;
        var entry = await db.AuditLogs.AsNoTracking().SingleAsync(a =>
            a.Action == PayrollAuditAction.PayslipPdfGenerated && a.ResourceId == slipId.ToString());
        entry.ResourceType.Should().Be(PayrollAuditAction.ResourceType.PayrollSlip);
        entry.TenantId.Should().Be(_tenantA);
        entry.UserId.Should().Be(PayrollAuditAction.SystemActorUserId);   // BR-7 job actor.
    }

    // ── BR-1: generate rejected for a non-ready run ─────────────────────────

    [Fact]
    public async Task Generate_NonReadyRun_Returns400()
    {
        var (runId, _) = await SeedRunWithSlip(_tenantA, "EMP-1", status: PayrollRunStatus.Queued);
        var mediator = Provider(_tenantA).GetRequiredService<IMediator>();

        var result = await mediator.Send(new GeneratePayslipsCommand(runId));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("run_not_ready_for_payslips");
    }

    // ── FR-6: single download streams the PDF with the BR-5 file name ────────

    [Fact]
    public async Task DownloadOne_AfterGeneration_StreamsPdf_WithBr5FileName()
    {
        var (runId, empId) = await SeedRunWithSlip(_tenantA, "EMP-7");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Send(new GeneratePayslipsCommand(runId));
        await provider.GetRequiredService<IPayslipBatchRenderer>().RenderRunAsync(runId);

        var download = await mediator.Send(new DownloadPayslipQuery(runId, empId));

        download.IsSuccess.Should().BeTrue();
        download.Value!.ContentType.Should().Be("application/pdf");
        download.Value.FileName.Should().Be("EMP-7_5_2026.pdf");
        download.Value.Content.Should().NotBeNullOrEmpty();
    }

    // ── AC-4: tenant A cannot download tenant B's payslip ────────────────────

    [Fact]
    public async Task DownloadOne_CrossTenant_Returns404()
    {
        // Tenant B generates a payslip.
        var (runIdB, empIdB) = await SeedRunWithSlip(_tenantB, "B-1");
        var providerB = Provider(_tenantB);
        await providerB.GetRequiredService<IMediator>().Send(new GeneratePayslipsCommand(runIdB));
        await providerB.GetRequiredService<IPayslipBatchRenderer>().RenderRunAsync(runIdB);

        // Tenant A tries to download tenant B's payslip by ids — the global query filter hides it (404).
        var mediatorA = Provider(_tenantA).GetRequiredService<IMediator>();
        var crossTenant = await mediatorA.Send(new DownloadPayslipQuery(runIdB, empIdB));

        crossTenant.IsSuccess.Should().BeFalse();
        crossTenant.StatusCode.Should().Be(404);
    }

    // ── AC-5: regenerate overwrites + updates the timestamp ─────────────────

    [Fact]
    public async Task Regenerate_OverwritesPdf_AndUpdatesTimestamp()
    {
        var (runId, empId) = await SeedRunWithSlip(_tenantA, "EMP-9");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();
        var renderer = provider.GetRequiredService<IPayslipBatchRenderer>();

        await mediator.Send(new GeneratePayslipsCommand(runId));
        await renderer.RenderRunAsync(runId);

        DateTime firstGeneratedAt;
        using (var db = Db(_tenantA))
            firstGeneratedAt = (await db.PayrollSlips.FirstAsync(s => s.PayrollRunId == runId)).PdfGeneratedAt!.Value;

        await Task.Delay(5); // ensure UtcNow advances so the timestamp differs.

        // Regenerate: second generate reports Regenerated=true, render overwrites the same path.
        var regen = await mediator.Send(new GeneratePayslipsCommand(runId));
        regen.Value!.Regenerated.Should().BeTrue();
        await renderer.RenderRunAsync(runId);

        using (var db = Db(_tenantA))
        {
            var slip = await db.PayrollSlips.FirstAsync(s => s.PayrollRunId == runId);
            slip.PdfStatus.Should().Be(PayslipPdfStatus.Generated);
            slip.PdfGeneratedAt!.Value.Should().BeOnOrAfter(firstGeneratedAt);
            slip.PdfStoragePath.Should().Be($"payroll/{runId:D}/{empId:D}.pdf");
        }

        // Only one stored file for that slip path (overwrite, not append).
        _storage.Files.Keys.Count(k => k.Item2 == $"payroll/{runId:D}/{empId:D}.pdf").Should().Be(1);
    }

    // ── FR-6 / AC-3: bulk ZIP contains an entry per generated slip ───────────

    [Fact]
    public async Task DownloadAll_ReturnsZip_WithOneEntryPerSlip()
    {
        var (runId, _) = await SeedRunWithSlip(_tenantA, "EMP-Z");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Send(new GeneratePayslipsCommand(runId));
        await provider.GetRequiredService<IPayslipBatchRenderer>().RenderRunAsync(runId);

        var zip = await mediator.Send(new DownloadAllPayslipsQuery(runId));

        zip.IsSuccess.Should().BeTrue();
        zip.Value!.ContentType.Should().Be("application/zip");

        using var archive = new ZipArchive(new MemoryStream(zip.Value.Content), ZipArchiveMode.Read);
        archive.Entries.Should().ContainSingle();
        archive.Entries[0].Name.Should().Be("EMP-Z_5_2026.pdf");
    }

    // ── §8: per-run payslip list carries employee + net + pdf status ────────

    [Fact]
    public async Task ListForRun_ReturnsRowPerSlip_WithEmployeeAndStatus()
    {
        var (runId, empId) = await SeedRunWithSlip(_tenantA, "EMP-L");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // Before generation: one row, status null (FE treats as pending).
        var before = await mediator.Send(new ListRunPayslipsQuery(runId));
        before.IsSuccess.Should().BeTrue();
        before.Value!.Should().ContainSingle();
        var row = before.Value[0];
        row.EmployeeId.Should().Be(empId);
        row.EmployeeNo.Should().Be("EMP-L");
        row.EmployeeName.Should().Be("Jane Doe");
        row.Department.Should().Be("Engineering");
        row.NetSalary.Should().Be(60_000m);
        row.PdfStatus.Should().BeNull();

        // After generation: status flips to Generated.
        await mediator.Send(new GeneratePayslipsCommand(runId));
        await provider.GetRequiredService<IPayslipBatchRenderer>().RenderRunAsync(runId);

        var after = await mediator.Send(new ListRunPayslipsQuery(runId));
        after.Value![0].PdfStatus.Should().Be(PayslipPdfStatus.Generated);
        after.Value[0].PdfFileSizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ListForRun_CrossTenant_Returns404()
    {
        var (runIdB, _) = await SeedRunWithSlip(_tenantB, "B-L");
        var mediatorA = Provider(_tenantA).GetRequiredService<IMediator>();

        var result = await mediatorA.Send(new ListRunPayslipsQuery(runIdB));

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(404);
    }

    // ── FR-7: status counts reflect generated vs pending ────────────────────

    [Fact]
    public async Task Status_AfterGeneration_ReportsGeneratedCount()
    {
        var (runId, _) = await SeedRunWithSlip(_tenantA, "EMP-S");
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Send(new GeneratePayslipsCommand(runId));

        var pending = await mediator.Send(new GetPayslipGenerationStatusQuery(runId));
        pending.Value!.Pending.Should().Be(1);
        pending.Value.Generated.Should().Be(0);
        pending.Value.IsComplete.Should().BeFalse();

        await provider.GetRequiredService<IPayslipBatchRenderer>().RenderRunAsync(runId);

        var done = await mediator.Send(new GetPayslipGenerationStatusQuery(runId));
        done.Value!.Generated.Should().Be(1);
        done.Value.Pending.Should().Be(0);
        done.Value.IsComplete.Should().BeTrue();
    }
}
