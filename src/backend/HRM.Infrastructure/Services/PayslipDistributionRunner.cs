using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Payroll;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using PA = HRM.Domain.Payroll.PayrollAuditAction;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace HRM.Infrastructure.Services;

/// <summary>
/// The compute side of bulk payslip-email distribution (US-PAY-011 FR-2/FR-8). Invoked by the Hangfire
/// SendPayslipEmailsJob (after it restores the tenant context, so the EF global query filter scopes every
/// query to the run's tenant — AC-5) or directly in tests. For each employee in a FINALIZED run with a
/// generated PDF: loads the PDF from <see cref="IFileStorage"/>, sends one email via
/// <see cref="IPayslipEmailSender"/> with Polly retry (NFR-2), and writes a <c>PayslipEmailLog</c> row.
///
/// <para>AC-3: an employee with no email on file is recorded <c>Skipped</c> + a warning, and the loop
/// continues. NFR-3 idempotency: an employee whose latest log row is already <c>Sent</c> is not re-sent.
/// AC-4: a permanent send failure is recorded <c>Failed</c> with the reason; transient failures are retried up
/// to <see cref="MaxRetries"/> with exponential backoff before being treated as permanent. FR-6: a documented
/// throttle bounds the send rate (real SMTP provider throttling is deferred).</para>
/// </summary>
public sealed class PayslipDistributionRunner : IPayslipDistributionRunner
{
    /// <summary>NFR-2/AC-4: Polly retries a transient send up to this many times (exponential backoff).</summary>
    private const int MaxRetries = 3;

    /// <summary>
    /// FR-6 rate-limit hook: cap on emails dispatched per minute. A documented throttle (the runner sleeps to
    /// hold this rate); real per-tenant SMTP-provider throttling is deferred. 0 disables the throttle (tests).
    /// </summary>
    private const int MaxEmailsPerMinute = 0; // throttle off by default; see FR-6 deferral in the module note.

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IFileStorage _fileStorage;
    private readonly IPayslipEmailSender _emailSender;
    private readonly IPayrollAuditLogger _audit;
    private readonly ILogger<PayslipDistributionRunner> _logger;

    public PayslipDistributionRunner(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IFileStorage fileStorage,
        IPayslipEmailSender emailSender,
        IPayrollAuditLogger audit,
        ILogger<PayslipDistributionRunner> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _fileStorage = fileStorage;
        _emailSender = emailSender;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// Thin convenience for non-job callers (tests): runs the READ → per-item WORK → per-send WRITE phases in
    /// sequence (identical result to the pre-ISSUE-269 single-method version). The Hangfire job orchestrates the
    /// phases so each SMTP send runs outside any tenant-GUC transaction and each result is committed per-send.
    /// </summary>
    public async Task<Result<PayslipDistributionSummaryDto>> RunAsync(
        Guid runId, IReadOnlyCollection<Guid>? targetEmployeeIds, CancellationToken cancellationToken = default)
    {
        var planResult = await LoadSendPlanAsync(runId, targetEmployeeIds, cancellationToken);
        if (planResult.IsFailure)
            return Result<PayslipDistributionSummaryDto>.Failure(
                planResult.Error!, planResult.StatusCode ?? 400, planResult.ErrorCode);

        var plan = planResult.Value!;

        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // NFR-3: never re-send an employee already Sent for this run (idempotent/resumable).
            if (plan.AlreadySent.Contains(item.EmployeeId))
                continue;

            var outcome = await SendOneAsync(item, cancellationToken);
            await PersistSendOutcomeAsync(outcome, cancellationToken);
        }

        _logger.LogInformation(
            "Payslip email distribution batch complete. RunId={RunId}, Tenant={TenantId}, Targeted={Targeted}.",
            runId, plan.TenantId, plan.Items.Count);

        return Result<PayslipDistributionSummaryDto>.Success(await SummaryFor(runId, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<PayslipSendPlan>> LoadSendPlanAsync(
        Guid runId, IReadOnlyCollection<Guid>? targetEmployeeIds, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipSendPlan>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayslipSendPlan>.Failure("Payroll run not found.", 404, "run_not_found");

        // BR-1: only Finalized runs are distributable. (The enqueue path already guards this; defence in depth.)
        if (run.Status != PayrollRunStatus.Finalized)
            return Result<PayslipSendPlan>.Failure(
                "Payslip emails can only be sent for a Finalized payroll run.", 409, "run_not_finalized");

        var tenantId = _tenantContext.TenantId;
        var companyName = string.IsNullOrWhiteSpace(_tenantContext.Subdomain) ? "Company" : _tenantContext.Subdomain;
        // BR-4: the tenant's CONFIGURED payslip sender ("From"), else null → SmtpEmailSender system default.
        var fromAddress = await ResolveFromAddressAsync(cancellationToken);

        // ISSUE-269: load slips AsNoTracking + project — no tracked entities carried into the WORK phase.
        var slips = await _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.PayrollRunId == runId)
            .Select(s => new { s.Id, s.EmployeeId, s.PayMonth, s.PayYear, s.PdfStatus, s.PdfStoragePath })
            .ToListAsync(cancellationToken);

        // Optional FR-4 targeting: a selective re-send only touches the named employees.
        if (targetEmployeeIds is { Count: > 0 })
        {
            var set = targetEmployeeIds.ToHashSet();
            slips = slips.Where(s => set.Contains(s.EmployeeId)).ToList();
        }

        if (slips.Count == 0)
            return Result<PayslipSendPlan>.Success(new PayslipSendPlan(
                runId, tenantId, companyName!, fromAddress, [], new HashSet<Guid>()));

        var employeeIds = slips.Select(s => s.EmployeeId).Distinct().ToList();
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeNo, e.FirstName, e.LastName, e.Email })
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        // Existing log rows for these employees in this run (NFR-3 resume + selective re-send overwrite).
        var existingLogs = (await _dbContext.PayslipEmailLogs.AsNoTracking()
                .Where(l => l.PayrollRunId == runId && employeeIds.Contains(l.EmployeeId))
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.CreatedAt).First());

        var alreadySent = existingLogs
            .Where(kv => kv.Value.Status == EmailDeliveryStatus.Sent)
            .Select(kv => kv.Key)
            .ToHashSet();

        var items = slips.Select(s =>
        {
            employees.TryGetValue(s.EmployeeId, out var employee);
            var employeeName = employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim();
            var employeeNo = employee?.EmployeeNo ?? s.EmployeeId.ToString();
            existingLogs.TryGetValue(s.EmployeeId, out var existing);
            return new PayslipSendItem(
                runId, tenantId, s.Id, s.EmployeeId, employee?.Email?.Trim(), employeeName, employeeNo,
                s.PayMonth, s.PayYear, companyName!, fromAddress,
                s.PdfStatus == PayslipPdfStatus.Generated && !string.IsNullOrWhiteSpace(s.PdfStoragePath),
                s.PdfStoragePath, existing?.RetryCount ?? 0);
        }).ToList();

        return Result<PayslipSendPlan>.Success(new PayslipSendPlan(
            runId, tenantId, companyName!, fromAddress, items, alreadySent));
    }

    /// <inheritdoc />
    public async Task<PayslipSendOutcome> SendOneAsync(PayslipSendItem item, CancellationToken cancellationToken = default)
    {
        // AC-3: no email on file -> Skipped + warning (no DB, no send).
        if (string.IsNullOrWhiteSpace(item.RecipientEmail))
        {
            _logger.LogWarning(
                "Payslip email skipped: employee {EmployeeId} has no email on file (run {RunId}, tenant {TenantId}).",
                item.EmployeeId, item.RunId, item.TenantId);
            return new PayslipSendOutcome(item.RunId, item.PayrollSlipId, item.EmployeeId, string.Empty,
                EmailDeliveryStatus.Skipped, SentAt: null, FailureReason: "No email address on file.", RetryCount: 0);
        }

        // BR-7/AC-2: the PDF must exist; a slip without a generated PDF cannot be emailed -> Failed. A storage
        // fault here propagates (a real mid-batch crash); already-persisted sends survive it (phase 3 committed them).
        byte[]? pdf = null;
        if (item.PdfGenerated)
            pdf = await ReadPdfAsync(item.PdfStoragePath!, cancellationToken);

        if (pdf is null)
        {
            _logger.LogWarning(
                "Payslip email failed: no generated PDF for employee {EmployeeId} (run {RunId}, tenant {TenantId}).",
                item.EmployeeId, item.RunId, item.TenantId);
            return new PayslipSendOutcome(item.RunId, item.PayrollSlipId, item.EmployeeId, item.RecipientEmail,
                EmailDeliveryStatus.Failed, SentAt: null, FailureReason: "Payslip PDF has not been generated.",
                RetryCount: item.RetryCountBaseline);
        }

        var fileName = PayslipStoragePath.DownloadFileName(item.EmployeeNo, item.PayMonth, item.PayYear);
        var message = new PayslipEmailMessage(
            item.TenantId, item.RecipientEmail, item.EmployeeName,
            PayslipEmailTemplate.BuildSubject(item.PayMonth, item.PayYear),
            PayslipEmailTemplate.BuildBody(item.EmployeeName, item.PayMonth, item.PayYear, item.CompanyName),
            fileName, pdf, "application/pdf", item.FromAddress);

        var retryPolicy = BuildRetryPolicy();
        var attempts = 0;
        PayslipSendOutcome outcome;
        try
        {
            await retryPolicy.ExecuteAsync(async ct =>
            {
                attempts++;
                await _emailSender.SendAsync(message, ct);
            }, cancellationToken);

            outcome = new PayslipSendOutcome(item.RunId, item.PayrollSlipId, item.EmployeeId, item.RecipientEmail,
                EmailDeliveryStatus.Sent, SentAt: DateTime.UtcNow, FailureReason: null, RetryCount: attempts - 1);
        }
        catch (Exception ex)
        {
            // AC-4: permanent failure (or transient exhausted after MaxRetries) -> Failed + reason.
            _logger.LogError(ex,
                "Payslip email failed permanently for employee {EmployeeId} after {Attempts} attempt(s) " +
                "(run {RunId}, tenant {TenantId}).", item.EmployeeId, attempts, item.RunId, item.TenantId);
            outcome = new PayslipSendOutcome(item.RunId, item.PayrollSlipId, item.EmployeeId, item.RecipientEmail,
                EmailDeliveryStatus.Failed, SentAt: null, FailureReason: Truncate(ex.Message, 2000),
                RetryCount: Math.Max(attempts - 1, 0));
        }

        // FR-6 throttle (documented; off by default) — held in the WORK phase, only after an actual send attempt.
        var throttle = MaxEmailsPerMinute > 0 ? TimeSpan.FromMinutes(1) / MaxEmailsPerMinute : TimeSpan.Zero;
        if (throttle > TimeSpan.Zero)
            await Task.Delay(throttle, cancellationToken);

        return outcome;
    }

    /// <inheritdoc />
    public async Task PersistSendOutcomeAsync(PayslipSendOutcome outcome, CancellationToken cancellationToken = default)
    {
        // Upsert the (run, employee) row fresh so a re-send overwrites the prior status rather than accumulating
        // rows (the summary reads the per-employee latest state). Committing per-send is what makes the batch
        // resumable — a crash after this returns cannot lose an already-recorded send (ISSUE-269).
        var existing = await _dbContext.PayslipEmailLogs
            .Where(l => l.PayrollRunId == outcome.RunId && l.EmployeeId == outcome.EmployeeId)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            _dbContext.PayslipEmailLogs.Add(new PayslipEmailLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                PayrollRunId = outcome.RunId,
                PayrollSlipId = outcome.PayrollSlipId,
                EmployeeId = outcome.EmployeeId,
                RecipientEmail = outcome.Recipient,
                Status = outcome.Status,
                SentAt = outcome.SentAt,
                FailureReason = outcome.FailureReason,
                RetryCount = outcome.RetryCount,
            });
        }
        else
        {
            existing.PayrollSlipId = outcome.PayrollSlipId;
            existing.RecipientEmail = outcome.Recipient;
            existing.Status = outcome.Status;
            existing.SentAt = outcome.SentAt;
            existing.FailureReason = outcome.FailureReason;
            existing.RetryCount = outcome.RetryCount;
        }

        // US-PAY-012 (BUG-080): audit a successful payslip email send (only when actually Sent — Skipped/Failed
        // are not "email sent"). Job-driven → system actor (BR-7); resourceId is the payslip id (there is no
        // PayslipEmailLog resource type). Staged into THIS per-send SaveChanges so it commits with the log row.
        if (outcome.Status == EmailDeliveryStatus.Sent)
            _audit.Log(PA.PayslipEmailSent, PA.ResourceType.PayrollSlip,
                outcome.PayrollSlipId.ToString(),
                before: null,
                after: new { outcome.RunId, outcome.PayrollSlipId, outcome.EmployeeId, outcome.SentAt },
                systemActor: true);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.ChangeTracker.Clear(); // keep the tracker bounded across per-send commits of a large run.
    }

    /// <summary>NFR-2/AC-4: retry only transient failures, with exponential backoff (2^n seconds).</summary>
    private static AsyncRetryPolicy BuildRetryPolicy()
        => Policy
            .Handle<PayslipEmailTransientException>()
            .WaitAndRetryAsync(MaxRetries, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

    private async Task<PayslipDistributionSummaryDto> SummaryFor(Guid runId, CancellationToken ct)
    {
        var logs = await _dbContext.PayslipEmailLogs.AsNoTracking()
            .Where(l => l.PayrollRunId == runId)
            .ToListAsync(ct);

        var slipCount = await _dbContext.PayrollSlips.AsNoTracking()
            .CountAsync(s => s.PayrollRunId == runId, ct);

        var sent = logs.Count(l => l.Status == EmailDeliveryStatus.Sent);
        var failed = logs.Count(l => l.Status == EmailDeliveryStatus.Failed);
        var skipped = logs.Count(l => l.Status == EmailDeliveryStatus.Skipped);
        var queuedRows = logs.Count(l => l.Status == EmailDeliveryStatus.Queued);
        // Employees with no log row yet are still "to send" -> counted as queued for the §7 total reconciliation.
        var notYetLogged = Math.Max(slipCount - logs.Count, 0);
        var queued = queuedRows + notYetLogged;

        var empIds = logs.Select(l => l.EmployeeId).Distinct().ToList();
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeNo, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id, ct);

        var recipients = logs
            .OrderBy(l => employees.TryGetValue(l.EmployeeId, out var e) ? $"{e.FirstName} {e.LastName}" : string.Empty)
            .Select(l =>
            {
                employees.TryGetValue(l.EmployeeId, out var e);
                return new PayslipRecipientStatusDto
                {
                    EmployeeId = l.EmployeeId,
                    EmployeeNo = e?.EmployeeNo ?? string.Empty,
                    EmployeeName = e is null ? string.Empty : $"{e.FirstName} {e.LastName}".Trim(),
                    RecipientEmail = l.RecipientEmail,
                    Status = l.Status.ToString(),
                    FailureReason = l.FailureReason,
                    SentAt = l.SentAt,
                    RetryCount = l.RetryCount,
                };
            })
            .ToList();

        return new PayslipDistributionSummaryDto
        {
            RunId = runId,
            TotalEmployees = slipCount,
            EmailsSent = sent,
            EmailsFailed = failed,
            EmailsSkipped = skipped,
            EmailsQueued = queued,
            StartedAt = logs.Count == 0 ? null : logs.Min(l => l.CreatedAt),
            CompletedAt = queued == 0 && logs.Count > 0
                ? logs.Where(l => l.SentAt != null).Select(l => l.SentAt).DefaultIfEmpty(null).Max()
                : null,
            HasSent = logs.Count > 0,
            IsSending = logs.Count > 0 && queuedRows > 0,
            Recipients = recipients,
        };
    }

    /// <summary>Reads a stored PDF into memory via the tenant-isolated storage abstraction (path is GUID-derived).</summary>
    private async Task<byte[]?> ReadPdfAsync(string relativePath, CancellationToken ct)
    {
        PayslipStoragePath.AssertSafe(relativePath);
        await using var stream = await _fileStorage.OpenReadAsync(_tenantContext.TenantId, relativePath, ct);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    /// <summary>
    /// BR-4 (ISSUE-229): the tenant's CONFIGURED payslip sender ("From") address. Reads
    /// <see cref="Tenant.PayrollFromEmail"/> for the run's tenant (resolved via <c>_tenantContext.TenantId</c> —
    /// the job restores the tenant context before this runs, so the query is tenant-scoped). Returns the
    /// configured address when non-blank, else null so <c>SmtpEmailSender</c> uses the system default sender.
    /// Never auto-derived from the subdomain (SPF/DKIM deliverability risk) — BR-4 requires a CONFIGURED value.
    /// </summary>
    private async Task<string?> ResolveFromAddressAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.TenantId;
        var configured = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.PayrollFromEmail)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
