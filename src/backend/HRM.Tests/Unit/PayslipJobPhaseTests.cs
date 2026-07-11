// ============================================================================
// ISSUE-269: the payslip render + distribution jobs are split into READ → WORK(no tx) → WRITE(per short unit)
// so the heavy PDF render/upload + SMTP send no longer sit inside one long tenant-GUC transaction (RLS
// idle-in-transaction). These tests pin the two behaviours the split adds on top of the existing (unchanged)
// payslip suites:
//
//   1. Job C resumability (the real crash-fix): the send loop commits each PayslipEmailLog row PER SEND. When a
//      mid-batch crash occurs (a storage/infra fault propagating out of the WORK phase), the already-sent rows
//      ARE persisted as Sent — so a re-run skips them. With the OLD end-of-batch SaveChanges every in-memory log
//      row before the crash was lost and everyone got re-sent.
//   2. Job B chunked writes: the render job persists results per ~200-slip chunk (ChangeTracker.Clear() between
//      chunks). Persisting across multiple chunks still lands every slip Generated with its path.
//
// PROVIDER: EF InMemory through the real runner/renderer + fakes for IFileStorage and IPayslipEmailSender — the
// same rationale/stack as the existing payslip suites (the verify gate runs `dotnet test` with no PostgreSQL /
// Docker / SMTP). The job's phase orchestration is reproduced directly on the services (as the Hangfire job
// does), so no live Hangfire server / ITenantJobRunner is needed — under RLS-off the runner is a pass-through.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Payroll;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Unit;

public sealed class PayslipJobPhaseTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenant = Guid.NewGuid();

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

    /// <summary>In-memory storage that can be told to THROW on one (tenant, path) — simulates a mid-batch crash.</summary>
    private sealed class CrashingFileStorage : IFileStorage
    {
        public Dictionary<(Guid, string), byte[]> Files { get; } = new();
        public (Guid Tenant, string Path)? ThrowOn { get; set; }

        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken ct = default)
        { using var m = new MemoryStream(); content.CopyTo(m); Files[(tenantId, relativePath)] = m.ToArray(); return Task.FromResult($"/{tenantId}/{relativePath}"); }

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken ct = default)
        {
            if (ThrowOn is { } t && t.Tenant == tenantId && t.Path == relativePath)
                throw new IOException("simulated storage crash mid-batch");
            return Task.FromResult<Stream?>(Files.TryGetValue((tenantId, relativePath), out var b) ? new MemoryStream(b, false) : null);
        }

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null) => "/x";
        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken ct = default) { Files.Remove((tenantId, relativePath)); return Task.CompletedTask; }
    }

    private sealed class RecordingEmailSender : IPayslipEmailSender
    {
        public List<PayslipEmailMessage> Sent { get; } = new();
        public Task SendAsync(PayslipEmailMessage message, CancellationToken cancellationToken = default)
        { Sent.Add(message); return Task.CompletedTask; }
    }

    private AppDbContext NewDb() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options,
        new MutableTenantContext { TenantId = _tenant });

    private PayslipDistributionRunner DistributionRunner(IFileStorage storage, IPayslipEmailSender sender)
        => new(NewDb(), new MutableTenantContext { TenantId = _tenant }, storage, sender,
            NullLogger<PayslipDistributionRunner>.Instance);

    private PayslipBatchRenderer BatchRenderer(IFileStorage storage)
        => new(NewDb(), new MutableTenantContext { TenantId = _tenant }, storage,
            NullLogger<PayslipBatchRenderer>.Instance);

    // ── Job C resumability: per-send commit survives a mid-batch crash ──────────────────────────────────────

    [Fact]
    [Trait("TC", "TC-PAY-269-C")]
    public async Task JobC_PerSendCommit_PersistsAlreadySentRows_WhenMidBatchCrash_AndReRunSkipsThem()
    {
        var storage = new CrashingFileStorage();
        var sender = new RecordingEmailSender();
        var runId = await SeedFinalizedRun(storage, 5); // 5 employees, all with email + a generated PDF.

        // Phase 1 (READ) once, then reproduce the JOB loop: WORK (send) → WRITE (persist) per employee.
        var loadRunner = DistributionRunner(storage, sender);
        var plan = (await loadRunner.LoadSendPlanAsync(runId, null)).Value!;
        plan.Items.Should().HaveCount(5);

        // Make the 3rd employee (in plan order) crash: its PDF read throws — a fault escaping the WORK phase.
        var crashItem = plan.Items[2];
        storage.ThrowOn = (_tenant, crashItem.PdfStoragePath!);

        // The phase methods must share ONE DbContext (as the job's DI scope does), so drive them off one runner.
        var runner = DistributionRunner(storage, sender);
        var processedBeforeCrash = new List<Guid>();
        Func<Task> loop = async () =>
        {
            foreach (var item in plan.Items)
            {
                if (plan.AlreadySent.Contains(item.EmployeeId)) continue;
                var outcome = await runner.SendOneAsync(item);       // WORK — throws on the 3rd item.
                await runner.PersistSendOutcomeAsync(outcome);       // WRITE — per-send commit.
                processedBeforeCrash.Add(item.EmployeeId);
            }
        };

        await loop.Should().ThrowAsync<IOException>(); // the crash propagates, as a real infra fault would.

        // The two employees processed BEFORE the crash have committed Sent rows; the crash victim + rest do not.
        processedBeforeCrash.Should().Equal(plan.Items[0].EmployeeId, plan.Items[1].EmployeeId);
        using (var db = NewDb())
        {
            var logs = await db.PayslipEmailLogs.Where(l => l.PayrollRunId == runId).ToListAsync();
            logs.Should().HaveCount(2, "per-send commit persisted the sends BEFORE the crash (the old end-of-batch save lost them)");
            logs.Should().OnlyContain(l => l.Status == EmailDeliveryStatus.Sent && l.SentAt != null);
            logs.Select(l => l.EmployeeId).Should().BeEquivalentTo(new[] { plan.Items[0].EmployeeId, plan.Items[1].EmployeeId });
        }

        // Resume: clear the fault and re-run. The already-Sent two are skipped; only the remaining three are sent.
        storage.ThrowOn = null;
        var resume = await DistributionRunner(storage, sender).RunAsync(runId, null);
        resume.IsSuccess.Should().BeTrue();

        // First pass sent 2 (items 0,1); the resume sends the other 3 — no duplicate for the first two.
        sender.Sent.Should().HaveCount(5);
        sender.Sent.Select(m => m.RecipientEmail).Should().OnlyHaveUniqueItems();
        using (var db = NewDb())
        {
            var logs = await db.PayslipEmailLogs.Where(l => l.PayrollRunId == runId).ToListAsync();
            logs.Should().HaveCount(5);
            logs.Should().OnlyContain(l => l.Status == EmailDeliveryStatus.Sent);
        }
    }

    // ── Job B chunked writes: results committed across multiple chunks still land every slip Generated ───────

    [Fact]
    [Trait("TC", "TC-PAY-269-B")]
    public async Task JobB_ChunkedWrites_AllSlipsGenerated_AcrossMultipleChunks()
    {
        var storage = new CrashingFileStorage();
        var (runId, slipIds) = await SeedRenderableRun(storage, 3);

        var renderer = BatchRenderer(storage);

        // Phase 1 (READ) + phase 2 (WORK) as the job does them.
        var plan = (await renderer.LoadRenderPlanAsync(runId)).Value!;
        plan.Items.Should().HaveCount(3);
        var outcomes = await renderer.RenderAndUploadAsync(plan);
        outcomes.Should().HaveCount(3);
        outcomes.Should().OnlyContain(o => o.Ok);

        // Phase 3 (WRITE) committed per SMALL chunk (chunk size 1) — 3 separate write transactions, with
        // ChangeTracker.Clear() between them. Every slip must still end Generated with its path.
        foreach (var chunk in outcomes.Chunk(1))
            await renderer.PersistRenderResultsAsync(chunk);

        using var db = NewDb();
        var slips = await db.PayrollSlips.Where(s => s.PayrollRunId == runId).ToListAsync();
        slips.Should().HaveCount(3);
        slips.Should().OnlyContain(s =>
            s.PdfStatus == PayslipPdfStatus.Generated
            && s.PdfStoragePath != null
            && s.PdfGeneratedAt != null
            && s.PdfFileSizeBytes > 0);
        slipIds.Should().BeSubsetOf(slips.Select(s => s.Id).ToList());
    }

    // ── seeding ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Finalized run with <paramref name="count"/> employees, each with a distinct email + a generated PDF.</summary>
    private async Task<Guid> SeedFinalizedRun(CrashingFileStorage storage, int count)
    {
        using var db = NewDb();
        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = _tenant, PayMonth = 5, PayYear = 2026, Status = PayrollRunStatus.Finalized,
            InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow, TotalEmployees = count, ProcessedEmployees = count,
        });

        for (var i = 0; i < count; i++)
        {
            var empId = BaseEntity.NewUuidV7();
            db.Employees.Add(new Employee
            {
                Id = empId, TenantId = _tenant, EmployeeNo = $"EMP-{i}", FirstName = "Jane", LastName = $"Doe{i}",
                Email = $"emp{i}@t.com", DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            });
            var path = PayslipStoragePath.ForSlip(runId, empId);
            db.PayrollSlips.Add(new PayrollSlip
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenant, PayrollRunId = runId, EmployeeId = empId,
                GrossEarnings = 70_000m, TotalDeductions = 10_000m, NetSalary = 60_000m,
                WorkingDays = 22m, PaidDays = 22m, PayMonth = 5, PayYear = 2026,
                PdfStatus = PayslipPdfStatus.Generated, PdfStoragePath = path,
            });
            storage.Files[(_tenant, path)] = "%PDF-1.4 fake"u8.ToArray();
        }

        await db.SaveChangesAsync();
        return runId;
    }

    /// <summary>ReviewPending run with <paramref name="count"/> renderable slips (no PDF yet) + details/dept/title.</summary>
    private async Task<(Guid RunId, List<Guid> SlipIds)> SeedRenderableRun(CrashingFileStorage storage, int count)
    {
        using var db = NewDb();
        var deptId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = _tenant, Name = "Engineering", Code = "ENG", IsActive = true });
        var jobId = BaseEntity.NewUuidV7();
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = _tenant, TitleName = "Engineer", IsActive = true });

        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = _tenant, PayMonth = 5, PayYear = 2026, Status = PayrollRunStatus.ReviewPending,
            InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow, TotalEmployees = count, ProcessedEmployees = count,
        });

        var slipIds = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            var empId = BaseEntity.NewUuidV7();
            db.Employees.Add(new Employee
            {
                Id = empId, TenantId = _tenant, EmployeeNo = $"EMP-{i}", FirstName = "Jane", LastName = $"Doe{i}",
                Email = $"emp{i}@t.com", DateOfJoining = new DateTime(2020, 1, 1), DepartmentId = deptId, JobTitleId = jobId,
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            });

            var slipId = BaseEntity.NewUuidV7();
            db.PayrollSlips.Add(new PayrollSlip
            {
                Id = slipId, TenantId = _tenant, PayrollRunId = runId, EmployeeId = empId,
                GrossEarnings = 70_000m, TotalDeductions = 10_000m, NetSalary = 60_000m,
                WorkingDays = 22m, PaidDays = 22m, LopDays = 0m, PayMonth = 5, PayYear = 2026,
            });
            db.PayrollSlipDetails.Add(new PayrollSlipDetail
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenant, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(),
                ComponentName = "Basic", ComponentType = nameof(SalaryComponentType.Earning), Amount = 50_000m,
            });
            slipIds.Add(slipId);
        }

        await db.SaveChangesAsync();
        return (runId, slipIds);
    }
}
