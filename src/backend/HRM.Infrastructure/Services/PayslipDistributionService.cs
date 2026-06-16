using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Bulk payslip-email distribution (US-PAY-011) — the ENQUEUE + SUMMARY side. All queries are tenant-scoped
/// via ITenantContext + the EF global query filter (AC-5). Enqueues the tenant-aware SendPayslipEmailsJob
/// (AC-1/FR-1) for FINALIZED runs with generated PDFs (BR-1), enforces the duplicate-send confirmation
/// (FR-7/BR-5), supports selective re-send (FR-4), and reports the per-run summary (BR-6/FR-5). The heavy
/// per-employee send loop lives in <see cref="IPayslipDistributionRunner"/>; when no Hangfire scheduler is
/// registered (tests/dev) the caller invokes the runner directly.
/// </summary>
public sealed class PayslipDistributionService : IPayslipDistributionService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPayslipDistributionRunner _runner;
    private readonly IPayslipDistributionJobScheduler? _jobScheduler;
    private readonly ILogger<PayslipDistributionService> _logger;

    public PayslipDistributionService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IPayslipDistributionRunner runner,
        ILogger<PayslipDistributionService> logger,
        IPayslipDistributionJobScheduler? jobScheduler = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _runner = runner;
        _logger = logger;
        _jobScheduler = jobScheduler;
    }

    public async Task<Result<PayslipDistributionAcceptedDto>> SendAsync(
        Guid runId, bool confirmResend, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipDistributionAcceptedDto>.Failure("Tenant context is not resolved.", 400);

        var guard = await GuardSendableAsync(runId, cancellationToken);
        if (guard.IsFailure)
            return Result<PayslipDistributionAcceptedDto>.Failure(guard.Error!, guard.StatusCode ?? 400, guard.ErrorCode);

        var (slipCount, generatedCount) = guard.Value;

        // FR-7/BR-5: if any payslip was already sent for the run, require explicit confirmation.
        var alreadySent = await _dbContext.PayslipEmailLogs.AsNoTracking()
            .AnyAsync(l => l.PayrollRunId == runId && l.Status == EmailDeliveryStatus.Sent, cancellationToken);

        if (alreadySent && !confirmResend)
            return Result<PayslipDistributionAcceptedDto>.Failure(
                "Payslips have already been sent for this run. Confirm to re-send.",
                409, "resend_requires_confirmation");

        Enqueue(runId, targetEmployeeIds: null);

        return Result<PayslipDistributionAcceptedDto>.Success(new PayslipDistributionAcceptedDto
        {
            RunId = runId,
            QueuedCount = slipCount,
            Resend = alreadySent,
        });
    }

    public async Task<Result<PayslipDistributionAcceptedDto>> ResendAsync(
        Guid runId, IReadOnlyCollection<Guid>? employeeIds, bool onlyFailed,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipDistributionAcceptedDto>.Failure("Tenant context is not resolved.", 400);

        var guard = await GuardSendableAsync(runId, cancellationToken);
        if (guard.IsFailure)
            return Result<PayslipDistributionAcceptedDto>.Failure(guard.Error!, guard.StatusCode ?? 400, guard.ErrorCode);

        IReadOnlyCollection<Guid>? targets;

        if (employeeIds is { Count: > 0 })
        {
            targets = employeeIds.Distinct().ToList();
        }
        else if (onlyFailed)
        {
            // FR-4: re-send to all employees whose last delivery for the run was Failed.
            var failed = await _dbContext.PayslipEmailLogs.AsNoTracking()
                .Where(l => l.PayrollRunId == runId && l.Status == EmailDeliveryStatus.Failed)
                .Select(l => l.EmployeeId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (failed.Count == 0)
                return Result<PayslipDistributionAcceptedDto>.Failure(
                    "There are no failed deliveries to re-send for this run.", 400, "no_failed_deliveries");

            targets = failed;
        }
        else
        {
            return Result<PayslipDistributionAcceptedDto>.Failure(
                "Specify employee ids or set onlyFailed to re-send.", 400, "no_resend_target");
        }

        Enqueue(runId, targets);

        return Result<PayslipDistributionAcceptedDto>.Success(new PayslipDistributionAcceptedDto
        {
            RunId = runId,
            QueuedCount = targets.Count,
            Resend = true,
        });
    }

    public async Task<Result<PayslipDistributionSummaryDto>> GetSummaryAsync(
        Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayslipDistributionSummaryDto>.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result<PayslipDistributionSummaryDto>.Failure("Payroll run not found.", 404, "run_not_found");

        var logs = await _dbContext.PayslipEmailLogs.AsNoTracking()
            .Where(l => l.PayrollRunId == runId)
            .ToListAsync(cancellationToken);

        var slipCount = await _dbContext.PayrollSlips.AsNoTracking()
            .CountAsync(s => s.PayrollRunId == runId, cancellationToken);

        // Employee display fields for the per-recipient list (§8 expandable Sent/Failed/Skipped rows).
        var empIds = logs.Select(l => l.EmployeeId).Distinct().ToList();
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeNo, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id, cancellationToken);

        var sent = logs.Count(l => l.Status == EmailDeliveryStatus.Sent);
        var failed = logs.Count(l => l.Status == EmailDeliveryStatus.Failed);
        var skipped = logs.Count(l => l.Status == EmailDeliveryStatus.Skipped);
        var queuedRows = logs.Count(l => l.Status == EmailDeliveryStatus.Queued);
        var notYetLogged = Math.Max(slipCount - logs.Count, 0);
        var queued = queuedRows + notYetLogged;

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

        return Result<PayslipDistributionSummaryDto>.Success(new PayslipDistributionSummaryDto
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
        });
    }

    /// <summary>
    /// BR-1: the run must exist, be Finalized, have slips, and have at least one generated PDF. Returns the
    /// (slipCount, generatedCount) when sendable.
    /// </summary>
    private async Task<Result<(int SlipCount, int GeneratedCount)>> GuardSendableAsync(Guid runId, CancellationToken ct)
    {
        var run = await _dbContext.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
            return Result<(int, int)>.Failure("Payroll run not found.", 404, "run_not_found");

        // BR-1: only Finalized runs.
        if (run.Status != PayrollRunStatus.Finalized)
            return Result<(int, int)>.Failure(
                "Payslip emails can only be sent for a Finalized payroll run.", 409, "run_not_finalized");

        var statuses = await _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => s.PayrollRunId == runId)
            .Select(s => s.PdfStatus)
            .ToListAsync(ct);

        if (statuses.Count == 0)
            return Result<(int, int)>.Failure("The run has no payslips to send.", 400, "no_slips");

        var generated = statuses.Count(s => s == PayslipPdfStatus.Generated);
        if (generated == 0)
            return Result<(int, int)>.Failure(
                "No payslip PDFs have been generated for this run. Generate payslips first.",
                400, "no_generated_payslips");

        return Result<(int, int)>.Success((statuses.Count, generated));
    }

    private void Enqueue(Guid runId, IReadOnlyCollection<Guid>? targetEmployeeIds)
    {
        if (_jobScheduler is not null)
        {
            _jobScheduler.Enqueue(_tenantContext.TenantId, _tenantContext.Subdomain, runId, targetEmployeeIds);
        }
        else
        {
            _logger.LogInformation(
                "Payslip email distribution for run {RunId} requested but no IPayslipDistributionJobScheduler is " +
                "registered — the runner must be invoked directly (dev/test).", runId);
        }
    }
}
