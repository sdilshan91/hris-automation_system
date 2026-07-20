// ============================================================================
// US-PAY-011: Bulk payslip email distribution — integration tests.
//
// Exercises the full handler -> service/runner -> DbContext path through a real DI container (MediatR pipeline
// + ITenantContext-driven global query filters), covering:
//   - AC-1/FR-1: send for a Finalized run with generated PDFs is accepted (202-equivalent) + the runner writes
//     a PayslipEmailLog row per employee with the right status; the fake sender is called per recipient with
//     the PDF attachment.
//   - AC-3: an employee without an email is Skipped; the rest are Sent.
//   - AC-5: tenant isolation — a send for tenant A does not touch tenant B's run/slips/logs.
//   - FR-4: re-send-failed only re-attempts the Failed rows.
//   - BR-6/FR-5: the distribution summary reports total/sent/failed/skipped/queued.
//
// PROVIDER: InMemory through the real composed pipeline (the verify gate runs `dotnet test` with no PostgreSQL
// / Docker / SMTP). A fake in-memory IFileStorage + a recording IPayslipEmailSender stand in for blob storage
// and SMTP. The Hangfire job is invoked via the runner directly, per the story brief.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Payroll;
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

public sealed class PayslipDistributionIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly InMemoryFileStorage _storage = new();
    private readonly RecordingEmailSender _sender = new();

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

    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Dictionary<(Guid, string), byte[]> Files { get; } = new();
        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken ct = default)
        { using var m = new MemoryStream(); content.CopyTo(m); Files[(tenantId, relativePath)] = m.ToArray(); return Task.FromResult($"/{tenantId}/{relativePath}"); }
        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken ct = default)
            => Task.FromResult<Stream?>(Files.TryGetValue((tenantId, relativePath), out var b) ? new MemoryStream(b, false) : null);
        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null) => "/x";
        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken ct = default) { Files.Remove((tenantId, relativePath)); return Task.CompletedTask; }
    }

    private sealed class RecordingEmailSender : IPayslipEmailSender
    {
        public HashSet<string> FailOnce { get; } = new();
        private readonly HashSet<string> _failedSoFar = new();
        public List<PayslipEmailMessage> Sent { get; } = new();

        public Task SendAsync(PayslipEmailMessage message, CancellationToken cancellationToken = default)
        {
            // A recipient in FailOnce fails permanently on its FIRST distribution, succeeds on a later re-send.
            if (FailOnce.Contains(message.RecipientEmail) && _failedSoFar.Add(message.RecipientEmail))
                throw new InvalidOperationException("forced failure");

            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private IServiceProvider Provider(Guid tenantId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(new MutableTenantContext { TenantId = tenantId });
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId });
        services.AddSingleton<IFileStorage>(_storage);
        services.AddSingleton<IPayslipEmailSender>(_sender);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IPayrollAuditLogger, PayrollAuditLogger>();
        services.AddScoped<IPayslipDistributionRunner, PayslipDistributionRunner>();
        services.AddScoped<IPayslipDistributionService, PayslipDistributionService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SendPayslipEmailsCommand).Assembly));
        return services.BuildServiceProvider();
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private async Task<(Guid RunId, List<(Guid EmpId, string No, string? Email)> Emps)> SeedFinalizedRun(
        Guid tenantId, params (string No, string? Email)[] employees)
    {
        using var db = Db(tenantId);
        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = tenantId, PayMonth = 5, PayYear = 2026, Status = PayrollRunStatus.Finalized,
            InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow, TotalEmployees = employees.Length, ProcessedEmployees = employees.Length,
        });

        var result = new List<(Guid, string, string?)>();
        foreach (var (no, email) in employees)
        {
            var empId = BaseEntity.NewUuidV7();
            db.Employees.Add(new Employee
            {
                Id = empId, TenantId = tenantId, EmployeeNo = no, FirstName = "Jane", LastName = "Doe",
                Email = email ?? string.Empty, DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            });
            var path = PayslipStoragePath.ForSlip(runId, empId);
            db.PayrollSlips.Add(new PayrollSlip
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollRunId = runId, EmployeeId = empId,
                GrossEarnings = 70_000m, TotalDeductions = 10_000m, NetSalary = 60_000m,
                WorkingDays = 22m, PaidDays = 22m, PayMonth = 5, PayYear = 2026,
                PdfStatus = PayslipPdfStatus.Generated, PdfStoragePath = path,
            });
            _storage.Files[(tenantId, path)] = "%PDF-1.4 fake"u8.ToArray();
            result.Add((empId, no, email));
        }
        await db.SaveChangesAsync();
        return (runId, result);
    }

    // ── AC-1 / FR-1: send is accepted, runner writes a log row per employee, sender called per recipient ──

    [Fact]
    public async Task Send_ThenRun_WritesLogPerEmployee_AndCallsSenderWithAttachment()
    {
        var (runId, emps) = await SeedFinalizedRun(_tenantA, ("EMP-1", "e1@t.com"), ("EMP-2", "e2@t.com"));
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        var accepted = await mediator.Send(new SendPayslipEmailsCommand(runId, false));
        accepted.IsSuccess.Should().BeTrue();
        accepted.Value!.QueuedCount.Should().Be(2);
        accepted.Value.Resend.Should().BeFalse();

        var run = await provider.GetRequiredService<IPayslipDistributionRunner>().RunAsync(runId, null);
        run.IsSuccess.Should().BeTrue();
        run.Value!.EmailsSent.Should().Be(2);

        _sender.Sent.Select(m => m.RecipientEmail).Should().BeEquivalentTo(new[] { "e1@t.com", "e2@t.com" });
        _sender.Sent.Should().OnlyContain(m => m.AttachmentContent.Length > 0 && m.AttachmentContentType == "application/pdf");
        _sender.Sent.Should().OnlyContain(m => m.AttachmentFileName.EndsWith("_05_2026.pdf")); // ISSUE-163: zero-padded month

        using var db = Db(_tenantA);
        var logs = await db.PayslipEmailLogs.Where(l => l.PayrollRunId == runId).ToListAsync();
        logs.Should().HaveCount(2);
        logs.Should().OnlyContain(l => l.Status == EmailDeliveryStatus.Sent && l.SentAt != null);
    }

    // ── US-PAY-012 / BUG-080: a successful send emits a system-actor audit ────

    [Fact]
    public async Task Run_SuccessfulSend_EmitsPayslipEmailSentAudit_AsSystemActor()
    {
        var (runId, emps) = await SeedFinalizedRun(_tenantA, ("EMP-1", "e1@t.com"), ("EMP-2", null));
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Send(new SendPayslipEmailsCommand(runId, false));
        var run = await provider.GetRequiredService<IPayslipDistributionRunner>().RunAsync(runId, null);
        run.Value!.EmailsSent.Should().Be(1);   // EMP-1 sent; EMP-2 (no email) skipped.

        using var db = Db(_tenantA);
        // Exactly one PayslipEmail.Sent audit — the Skipped employee produces no send audit.
        var sentAudits = await db.AuditLogs.AsNoTracking()
            .Where(a => a.Action == PayrollAuditAction.PayslipEmailSent
                        && a.ResourceType == PayrollAuditAction.ResourceType.PayrollSlip)
            .ToListAsync();
        sentAudits.Should().ContainSingle();
        sentAudits[0].TenantId.Should().Be(_tenantA);
        sentAudits[0].UserId.Should().Be(PayrollAuditAction.SystemActorUserId);   // BR-7 job actor.
    }

    // ── AC-3: an employee without an email is Skipped; the rest Sent ──────────

    [Fact]
    public async Task Run_SkipsEmployeeWithoutEmail()
    {
        var (runId, emps) = await SeedFinalizedRun(_tenantA, ("EMP-1", "e1@t.com"), ("EMP-2", null));
        var provider = Provider(_tenantA);
        await provider.GetRequiredService<IMediator>().Send(new SendPayslipEmailsCommand(runId, false));

        var run = await provider.GetRequiredService<IPayslipDistributionRunner>().RunAsync(runId, null);

        run.Value!.EmailsSent.Should().Be(1);
        run.Value.EmailsSkipped.Should().Be(1);

        using var db = Db(_tenantA);
        var skipped = await db.PayslipEmailLogs.SingleAsync(l => l.EmployeeId == emps[1].EmpId);
        skipped.Status.Should().Be(EmailDeliveryStatus.Skipped);
    }

    // ── AC-5: tenant isolation — tenant A's run does not touch tenant B ───────

    [Fact]
    public async Task Run_ForTenantA_DoesNotTouchTenantB()
    {
        var (runIdA, _) = await SeedFinalizedRun(_tenantA, ("A-1", "a1@t.com"));
        var (runIdB, empsB) = await SeedFinalizedRun(_tenantB, ("B-1", "b1@t.com"));

        // Tenant A sends — should only ever see/affect tenant A's run.
        var providerA = Provider(_tenantA);
        await providerA.GetRequiredService<IMediator>().Send(new SendPayslipEmailsCommand(runIdA, false));
        await providerA.GetRequiredService<IPayslipDistributionRunner>().RunAsync(runIdA, null);

        // Tenant A trying to run tenant B's run id sees nothing (global filter hides it -> run_not_found).
        var crossTenant = await providerA.GetRequiredService<IPayslipDistributionRunner>().RunAsync(runIdB, null);
        crossTenant.IsSuccess.Should().BeFalse();
        crossTenant.StatusCode.Should().Be(404);

        // No tenant-B log rows were created.
        using var dbB = Db(_tenantB);
        (await dbB.PayslipEmailLogs.CountAsync(l => l.PayrollRunId == runIdB)).Should().Be(0);

        _sender.Sent.Should().OnlyContain(m => m.RecipientEmail == "a1@t.com");
    }

    // ── FR-4: re-send-failed only re-attempts the Failed rows ────────────────

    [Fact]
    public async Task ResendFailed_OnlyReAttemptsFailedRows()
    {
        // EMP-2 fails on the first distribution, then succeeds on the re-send.
        _sender.FailOnce.Add("e2@t.com");
        var (runId, emps) = await SeedFinalizedRun(_tenantA, ("EMP-1", "e1@t.com"), ("EMP-2", "e2@t.com"));
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.Send(new SendPayslipEmailsCommand(runId, false));
        var first = await provider.GetRequiredService<IPayslipDistributionRunner>().RunAsync(runId, null);
        first.Value!.EmailsSent.Should().Be(1);
        first.Value.EmailsFailed.Should().Be(1);

        var sentDuringFirst = _sender.Sent.Count; // EMP-1 only

        // Re-send only failed.
        var resend = await mediator.Send(new ResendPayslipEmailsCommand(runId, null, OnlyFailed: true));
        resend.IsSuccess.Should().BeTrue();
        resend.Value!.QueuedCount.Should().Be(1); // only EMP-2 targeted.

        await provider.GetRequiredService<IPayslipDistributionRunner>().RunAsync(runId, new[] { emps[1].EmpId });

        // Only EMP-2 was re-attempted (EMP-1 not re-sent).
        (_sender.Sent.Count - sentDuringFirst).Should().Be(1);
        _sender.Sent.Last().RecipientEmail.Should().Be("e2@t.com");

        using var db = Db(_tenantA);
        var logs = await db.PayslipEmailLogs.Where(l => l.PayrollRunId == runId).ToListAsync();
        logs.Should().OnlyContain(l => l.Status == EmailDeliveryStatus.Sent);
    }

    // ── BR-6 / FR-5: distribution summary reports the counts ─────────────────

    [Fact]
    public async Task Summary_ReportsCounts()
    {
        var (runId, _) = await SeedFinalizedRun(_tenantA, ("EMP-1", "e1@t.com"), ("EMP-2", null));
        var provider = Provider(_tenantA);
        var mediator = provider.GetRequiredService<IMediator>();

        // Before any send: all queued.
        var before = await mediator.Send(new GetPayslipDistributionSummaryQuery(runId));
        before.Value!.TotalEmployees.Should().Be(2);
        before.Value.EmailsQueued.Should().Be(2);
        before.Value.StartedAt.Should().BeNull();

        await mediator.Send(new SendPayslipEmailsCommand(runId, false));
        await provider.GetRequiredService<IPayslipDistributionRunner>().RunAsync(runId, null);

        var after = await mediator.Send(new GetPayslipDistributionSummaryQuery(runId));
        after.Value!.EmailsSent.Should().Be(1);
        after.Value.EmailsSkipped.Should().Be(1);
        after.Value.EmailsQueued.Should().Be(0);
        after.Value.StartedAt.Should().NotBeNull();
        after.Value.CompletedAt.Should().NotBeNull();
    }
}
