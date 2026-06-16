// ============================================================================
// US-PAY-011: Bulk payslip email distribution — unit tests.
//
//   - PayslipEmailTemplate: subject is "Your Payslip for {Month} {Year}" (AC-2); body substitutes the allowed
//     variables and contains NO salary amount (NFR-5).
//   - Runner: an employee with no email is recorded Skipped + the loop continues (AC-3).
//   - Runner: a transient send failure is retried via Polly and eventually succeeds (AC-4/NFR-2).
//   - Runner: a permanent (non-transient) send failure is recorded Failed with the reason (AC-4).
//   - Runner: idempotent — a re-run does not re-send an employee already Sent (NFR-3).
//   - Service: send is rejected (409) for a non-Finalized run (BR-1).
//   - Service: a second send for a run that already sent requires the confirm flag (FR-7/BR-5).
//
// PROVIDER: EF InMemory through the real runner/service + fakes for IFileStorage and IPayslipEmailSender (the
// verify gate runs `dotnet test` with no PostgreSQL / Docker / SMTP). The Hangfire job is invoked directly via
// the runner, per the story brief.
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

public sealed class PayslipDistributionTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenant = Guid.NewGuid();

    // ── Template (pure) ──────────────────────────────────────────────────────

    [Fact]
    public void Template_Subject_FollowsAc2Format()
    {
        PayslipEmailTemplate.BuildSubject(5, 2026).Should().Be("Your Payslip for May 2026");
    }

    [Fact]
    public void Template_Body_SubstitutesVariables_AndHasNoSalaryAmount()
    {
        var body = PayslipEmailTemplate.BuildBody("Jane Doe", 5, 2026, "Acme Corp");

        body.Should().Contain("Jane Doe");
        body.Should().Contain("May 2026");
        body.Should().Contain("Acme Corp");
        // NFR-5: no salary amounts in the body. Seed amounts that would appear if leaked.
        body.Should().NotContain("60000");
        body.Should().NotContain("70,000");
        body.Should().NotContainEquivalentOf("net salary");
    }

    // ── Fakes ────────────────────────────────────────────────────────────────

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

    private sealed class FakeFileStorage : IFileStorage
    {
        public Dictionary<(Guid, string), byte[]> Files { get; } = new();
        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken ct = default)
        { Files[(tenantId, relativePath)] = ReadAll(content); return Task.FromResult($"/{tenantId}/{relativePath}"); }
        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken ct = default)
            => Task.FromResult<Stream?>(Files.TryGetValue((tenantId, relativePath), out var b) ? new MemoryStream(b, false) : null);
        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null) => "/x";
        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken ct = default) { Files.Remove((tenantId, relativePath)); return Task.CompletedTask; }
        private static byte[] ReadAll(Stream s) { using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); }
    }

    /// <summary>Records every recipient; can be configured to fail (transient or permanent) for the first N attempts.</summary>
    private sealed class RecordingEmailSender : IPayslipEmailSender
    {
        private readonly Func<PayslipEmailMessage, int, Exception?>? _failPlan;
        private readonly Dictionary<string, int> _attemptsByRecipient = new();
        public List<PayslipEmailMessage> Sent { get; } = new();

        public RecordingEmailSender(Func<PayslipEmailMessage, int, Exception?>? failPlan = null) => _failPlan = failPlan;

        public int AttemptsFor(string recipient) => _attemptsByRecipient.GetValueOrDefault(recipient);

        public Task SendAsync(PayslipEmailMessage message, CancellationToken cancellationToken = default)
        {
            var attempt = _attemptsByRecipient.GetValueOrDefault(message.RecipientEmail) + 1;
            _attemptsByRecipient[message.RecipientEmail] = attempt;

            var ex = _failPlan?.Invoke(message, attempt);
            if (ex is not null) throw ex;

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private AppDbContext Db()
    {
        var ctx = new MutableTenantContext { TenantId = _tenant };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private PayslipDistributionRunner Runner(IPayslipEmailSender sender, FakeFileStorage storage)
        => new(Db(), new MutableTenantContext { TenantId = _tenant }, storage, sender, NullLogger<PayslipDistributionRunner>.Instance);

    private PayslipDistributionService Service(FakeFileStorage storage, IPayslipEmailSender sender)
        => new(Db(), new MutableTenantContext { TenantId = _tenant },
            new PayslipDistributionRunner(Db(), new MutableTenantContext { TenantId = _tenant }, storage, sender, NullLogger<PayslipDistributionRunner>.Instance),
            NullLogger<PayslipDistributionService>.Instance, jobScheduler: null);

    /// <summary>Seeds a Finalized run with one slip per (employeeNo, email); a null email means none on file.</summary>
    private async Task<(Guid RunId, List<(Guid EmpId, string No)> Emps)> SeedFinalizedRun(
        FakeFileStorage storage, params (string No, string? Email, bool Pdf)[] employees)
    {
        using var db = Db();
        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = _tenant, PayMonth = 5, PayYear = 2026, Status = PayrollRunStatus.Finalized,
            InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow, TotalEmployees = employees.Length, ProcessedEmployees = employees.Length,
        });

        var result = new List<(Guid, string)>();
        foreach (var (no, email, pdf) in employees)
        {
            var empId = BaseEntity.NewUuidV7();
            db.Employees.Add(new Employee
            {
                Id = empId, TenantId = _tenant, EmployeeNo = no, FirstName = "Jane", LastName = "Doe",
                Email = email ?? string.Empty, DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            });

            var path = PayslipStoragePath.ForSlip(runId, empId);
            db.PayrollSlips.Add(new PayrollSlip
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenant, PayrollRunId = runId, EmployeeId = empId,
                GrossEarnings = 70_000m, TotalDeductions = 10_000m, NetSalary = 60_000m,
                WorkingDays = 22m, PaidDays = 22m, PayMonth = 5, PayYear = 2026,
                PdfStatus = pdf ? PayslipPdfStatus.Generated : null,
                PdfStoragePath = pdf ? path : null,
            });
            if (pdf) storage.Files[(_tenant, path)] = "%PDF-1.4 fake"u8.ToArray();
            result.Add((empId, no));
        }

        await db.SaveChangesAsync();
        return (runId, result);
    }

    // ── AC-3: no email -> Skipped, loop continues ────────────────────────────

    [Fact]
    public async Task Runner_EmployeeWithoutEmail_RecordedSkipped_AndOthersStillSent()
    {
        var storage = new FakeFileStorage();
        var sender = new RecordingEmailSender();
        var (runId, emps) = await SeedFinalizedRun(storage,
            ("EMP-1", "emp1@t.com", true),
            ("EMP-2", null, true)); // no email

        var summary = await Runner(sender, storage).RunAsync(runId, null);

        summary.IsSuccess.Should().BeTrue();
        summary.Value!.EmailsSent.Should().Be(1);
        summary.Value.EmailsSkipped.Should().Be(1);
        sender.Sent.Should().ContainSingle().Which.RecipientEmail.Should().Be("emp1@t.com");

        using var db = Db();
        var logs = await db.PayslipEmailLogs.Where(l => l.PayrollRunId == runId).ToListAsync();
        logs.Should().HaveCount(2);
        logs.Single(l => l.EmployeeId == emps[1].EmpId).Status.Should().Be(EmailDeliveryStatus.Skipped);
    }

    // ── AC-4 / NFR-2: transient failure is retried then succeeds ─────────────

    [Fact]
    public async Task Runner_TransientFailure_RetriedViaPolly_ThenSucceeds()
    {
        var storage = new FakeFileStorage();
        // Fail the first 2 attempts transiently, succeed on the 3rd.
        var sender = new RecordingEmailSender((_, attempt) =>
            attempt <= 2 ? new PayslipEmailTransientException("smtp 4xx") : null);
        var (runId, _) = await SeedFinalizedRun(storage, ("EMP-1", "emp1@t.com", true));

        var summary = await Runner(sender, storage).RunAsync(runId, null);

        summary.Value!.EmailsSent.Should().Be(1);
        summary.Value.EmailsFailed.Should().Be(0);
        sender.AttemptsFor("emp1@t.com").Should().Be(3); // 2 retries + the success.

        using var db = Db();
        var log = await db.PayslipEmailLogs.SingleAsync(l => l.PayrollRunId == runId);
        log.Status.Should().Be(EmailDeliveryStatus.Sent);
        log.RetryCount.Should().Be(2); // attempts beyond the first.
    }

    // ── AC-4: permanent failure -> Failed + reason ───────────────────────────

    [Fact]
    public async Task Runner_PermanentFailure_RecordedFailed_WithReason_NoRetry()
    {
        var storage = new FakeFileStorage();
        var sender = new RecordingEmailSender((_, _) => new InvalidOperationException("hard bounce"));
        var (runId, _) = await SeedFinalizedRun(storage, ("EMP-1", "emp1@t.com", true));

        var summary = await Runner(sender, storage).RunAsync(runId, null);

        summary.Value!.EmailsFailed.Should().Be(1);
        summary.Value.EmailsSent.Should().Be(0);
        // Non-transient => Polly does not retry: exactly one attempt.
        sender.AttemptsFor("emp1@t.com").Should().Be(1);

        using var db = Db();
        var log = await db.PayslipEmailLogs.SingleAsync(l => l.PayrollRunId == runId);
        log.Status.Should().Be(EmailDeliveryStatus.Failed);
        log.FailureReason.Should().Contain("hard bounce");
    }

    // ── NFR-3: idempotent — re-run does not re-send an already-Sent employee ──

    [Fact]
    public async Task Runner_ReRun_DoesNotResendAlreadySentEmployee()
    {
        var storage = new FakeFileStorage();
        var sender = new RecordingEmailSender();
        var (runId, _) = await SeedFinalizedRun(storage, ("EMP-1", "emp1@t.com", true));

        await Runner(sender, storage).RunAsync(runId, null);
        await Runner(sender, storage).RunAsync(runId, null); // resume / re-run

        sender.Sent.Should().ContainSingle(); // sent once, not twice.

        using var db = Db();
        (await db.PayslipEmailLogs.CountAsync(l => l.PayrollRunId == runId)).Should().Be(1);
    }

    // ── BR-1: send rejected (409) for a non-Finalized run ────────────────────

    [Fact]
    public async Task Service_Send_NonFinalizedRun_Returns409()
    {
        var storage = new FakeFileStorage();
        var sender = new RecordingEmailSender();
        // Seed a ReviewPending run by hand (the seeder always finalizes, so override here).
        Guid runId;
        using (var db = Db())
        {
            runId = BaseEntity.NewUuidV7();
            db.PayrollRuns.Add(new PayrollRun
            {
                Id = runId, TenantId = _tenant, PayMonth = 5, PayYear = 2026, Status = PayrollRunStatus.ReviewPending,
                InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow, TotalEmployees = 1, ProcessedEmployees = 1,
            });
            await db.SaveChangesAsync();
        }

        var result = await Service(storage, sender).SendAsync(runId, confirmResend: false);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("run_not_finalized");
    }

    // ── FR-7 / BR-5: a second send requires confirmation ─────────────────────

    [Fact]
    public async Task Service_SecondSend_AfterAlreadySent_RequiresConfirmation()
    {
        var storage = new FakeFileStorage();
        var sender = new RecordingEmailSender();
        var (runId, _) = await SeedFinalizedRun(storage, ("EMP-1", "emp1@t.com", true));

        // First send + actually deliver (so a Sent log row exists).
        var first = await Service(storage, sender).SendAsync(runId, confirmResend: false);
        first.IsSuccess.Should().BeTrue();
        await Runner(sender, storage).RunAsync(runId, null);

        // Second send without confirm -> 409.
        var second = await Service(storage, sender).SendAsync(runId, confirmResend: false);
        second.IsSuccess.Should().BeFalse();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("resend_requires_confirmation");

        // With confirm -> accepted (Resend=true).
        var confirmed = await Service(storage, sender).SendAsync(runId, confirmResend: true);
        confirmed.IsSuccess.Should().BeTrue();
        confirmed.Value!.Resend.Should().BeTrue();
    }
}
