using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Payroll;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
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
    private readonly ILogger<PayslipDistributionRunner> _logger;

    public PayslipDistributionRunner(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IFileStorage fileStorage,
        IPayslipEmailSender emailSender,
        ILogger<PayslipDistributionRunner> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _fileStorage = fileStorage;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<Result<PayslipDistributionSummaryDto>> RunAsync(
        Guid runId, IReadOnlyCollection<Guid>? targetEmployeeIds, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipDistributionSummaryDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayslipDistributionSummaryDto>.Failure("Payroll run not found.", 404, "run_not_found");

        // BR-1: only Finalized runs are distributable. (The enqueue path already guards this; defence in depth.)
        if (run.Status != PayrollRunStatus.Finalized)
            return Result<PayslipDistributionSummaryDto>.Failure(
                "Payslip emails can only be sent for a Finalized payroll run.", 409, "run_not_finalized");

        var slips = await _dbContext.PayrollSlips
            .Where(s => s.PayrollRunId == runId)
            .ToListAsync(cancellationToken);

        // Optional FR-4 targeting: a selective re-send only touches the named employees.
        if (targetEmployeeIds is { Count: > 0 })
        {
            var set = targetEmployeeIds.ToHashSet();
            slips = slips.Where(s => set.Contains(s.EmployeeId)).ToList();
        }

        if (slips.Count == 0)
            return await GetSummaryInternalAsync(run, cancellationToken);

        var employeeIds = slips.Select(s => s.EmployeeId).Distinct().ToList();
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        // Existing log rows for these employees in this run (NFR-3 resume + selective re-send overwrite).
        var existingLogs = (await _dbContext.PayslipEmailLogs
                .Where(l => l.PayrollRunId == runId && employeeIds.Contains(l.EmployeeId))
                .ToListAsync(cancellationToken))
            .GroupBy(l => l.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.CreatedAt).First());

        var companyName = string.IsNullOrWhiteSpace(_tenantContext.Subdomain) ? "Company" : _tenantContext.Subdomain;
        var fromAddress = ResolveFromAddress(); // BR-4: tenant sender domain deferred -> system default.
        var tenantId = _tenantContext.TenantId;
        var retryPolicy = BuildRetryPolicy();

        var throttle = MaxEmailsPerMinute > 0 ? TimeSpan.FromMinutes(1) / MaxEmailsPerMinute : TimeSpan.Zero;

        foreach (var slip in slips)
        {
            cancellationToken.ThrowIfCancellationRequested();

            existingLogs.TryGetValue(slip.EmployeeId, out var existing);

            // NFR-3: never re-send an employee already Sent for this run (idempotent/resumable).
            if (existing is { Status: EmailDeliveryStatus.Sent })
                continue;

            employees.TryGetValue(slip.EmployeeId, out var employee);
            var email = employee?.Email?.Trim();

            // AC-3: no email on file -> Skipped + warning; loop continues.
            if (string.IsNullOrWhiteSpace(email))
            {
                _logger.LogWarning(
                    "Payslip email skipped: employee {EmployeeId} has no email on file (run {RunId}, tenant {TenantId}).",
                    slip.EmployeeId, runId, tenantId);
                UpsertLog(existing, slip, runId, tenantId, recipient: string.Empty,
                    EmailDeliveryStatus.Skipped, sentAt: null, failureReason: "No email address on file.",
                    retryCount: 0);
                continue;
            }

            // BR-7/AC-2: the PDF must exist; a slip without a generated PDF cannot be emailed -> Failed.
            byte[]? pdf = null;
            if (slip.PdfStatus == PayslipPdfStatus.Generated && !string.IsNullOrWhiteSpace(slip.PdfStoragePath))
                pdf = await ReadPdfAsync(slip.PdfStoragePath!, cancellationToken);

            if (pdf is null)
            {
                _logger.LogWarning(
                    "Payslip email failed: no generated PDF for employee {EmployeeId} (run {RunId}, tenant {TenantId}).",
                    slip.EmployeeId, runId, tenantId);
                UpsertLog(existing, slip, runId, tenantId, recipient: email,
                    EmailDeliveryStatus.Failed, sentAt: null, failureReason: "Payslip PDF has not been generated.",
                    retryCount: existing?.RetryCount ?? 0);
                continue;
            }

            var employeeName = employee is null ? "Employee" : $"{employee.FirstName} {employee.LastName}".Trim();
            var fileName = PayslipStoragePath.DownloadFileName(
                employee?.EmployeeNo ?? slip.EmployeeId.ToString(), slip.PayMonth, slip.PayYear);

            var message = new PayslipEmailMessage(
                tenantId, email, employeeName,
                PayslipEmailTemplate.BuildSubject(slip.PayMonth, slip.PayYear),
                PayslipEmailTemplate.BuildBody(employeeName, slip.PayMonth, slip.PayYear, companyName),
                fileName, pdf, "application/pdf", fromAddress);

            var attempts = 0;
            try
            {
                await retryPolicy.ExecuteAsync(async ct =>
                {
                    attempts++;
                    await _emailSender.SendAsync(message, ct);
                }, cancellationToken);

                UpsertLog(existing, slip, runId, tenantId, recipient: email,
                    EmailDeliveryStatus.Sent, sentAt: DateTime.UtcNow, failureReason: null,
                    retryCount: attempts - 1);
            }
            catch (Exception ex)
            {
                // AC-4: permanent failure (or transient exhausted after MaxRetries) -> Failed + reason.
                _logger.LogError(ex,
                    "Payslip email failed permanently for employee {EmployeeId} after {Attempts} attempt(s) " +
                    "(run {RunId}, tenant {TenantId}).", slip.EmployeeId, attempts, runId, tenantId);
                UpsertLog(existing, slip, runId, tenantId, recipient: email,
                    EmailDeliveryStatus.Failed, sentAt: null, failureReason: Truncate(ex.Message, 2000),
                    retryCount: Math.Max(attempts - 1, 0));
            }

            if (throttle > TimeSpan.Zero)
                await Task.Delay(throttle, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Payslip email distribution batch complete. RunId={RunId}, Tenant={TenantId}, Targeted={Targeted}.",
            runId, tenantId, slips.Count);

        return await GetSummaryInternalAsync(run, cancellationToken);
    }

    /// <summary>NFR-2/AC-4: retry only transient failures, with exponential backoff (2^n seconds).</summary>
    private static AsyncRetryPolicy BuildRetryPolicy()
        => Policy
            .Handle<PayslipEmailTransientException>()
            .WaitAndRetryAsync(MaxRetries, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

    /// <summary>
    /// Inserts a new log row or updates the existing one for this (run, employee) — so a re-send overwrites the
    /// prior status rather than accumulating rows (the summary reads the per-employee latest state).
    /// </summary>
    private void UpsertLog(
        PayslipEmailLog? existing, PayrollSlip slip, Guid runId, Guid tenantId, string recipient,
        string status, DateTime? sentAt, string? failureReason, int retryCount)
    {
        if (existing is null)
        {
            _dbContext.PayslipEmailLogs.Add(new PayslipEmailLog
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                PayrollRunId = runId,
                PayrollSlipId = slip.Id,
                EmployeeId = slip.EmployeeId,
                RecipientEmail = recipient,
                Status = status,
                SentAt = sentAt,
                FailureReason = failureReason,
                RetryCount = retryCount,
            });
        }
        else
        {
            existing.PayrollSlipId = slip.Id;
            existing.RecipientEmail = recipient;
            existing.Status = status;
            existing.SentAt = sentAt;
            existing.FailureReason = failureReason;
            existing.RetryCount = retryCount;
        }
    }

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

    private async Task<Result<PayslipDistributionSummaryDto>> GetSummaryInternalAsync(PayrollRun run, CancellationToken ct)
        => Result<PayslipDistributionSummaryDto>.Success(await SummaryFor(run.Id, ct));

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

    /// <summary>BR-4: the tenant sender ("From") address. No tenant sender-domain config surface exists yet, so
    /// this returns null (the sender uses the system default). Wire to a tenant payroll-settings entity later.</summary>
    private string? ResolveFromAddress() => null;

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
