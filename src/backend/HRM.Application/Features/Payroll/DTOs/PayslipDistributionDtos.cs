namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>
/// Result of accepting a send / re-send payslip-emails request (US-PAY-011 AC-1/FR-1). Returned as 202.
/// </summary>
public sealed record PayslipDistributionAcceptedDto
{
    public required Guid RunId { get; init; }

    /// <summary>Number of employees the enqueued job will attempt to email (the targeted count).</summary>
    public required int QueuedCount { get; init; }

    /// <summary>True when this run had already had payslips sent (a confirmed re-send, FR-7/BR-5).</summary>
    public required bool Resend { get; init; }
}

/// <summary>
/// Per-run email-distribution summary (US-PAY-011 BR-6/FR-5, §7 "Distribution Summary"). Backs the §8 summary
/// card the FE polls (SignalR live progress deferred).
/// </summary>
public sealed record PayslipDistributionSummaryDto
{
    public required Guid RunId { get; init; }

    /// <summary>Total employees in the run (§7 total_employees).</summary>
    public required int TotalEmployees { get; init; }

    /// <summary>Successfully sent (§7 emails_sent).</summary>
    public required int EmailsSent { get; init; }

    /// <summary>Failed after retries (§7 emails_failed).</summary>
    public required int EmailsFailed { get; init; }

    /// <summary>Skipped — no email on file (§7 emails_skipped).</summary>
    public required int EmailsSkipped { get; init; }

    /// <summary>Pending send (§7 emails_queued).</summary>
    public required int EmailsQueued { get; init; }

    /// <summary>Earliest log row created_at for the run, or null if distribution has not started (§7 started_at).</summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>Latest sent_at across the run's rows once nothing is queued, else null (§7 completed_at).</summary>
    public DateTime? CompletedAt { get; init; }

    /// <summary>True once at least one email has been Sent for this run (FE re-send confirmation gate, FR-7/BR-5).</summary>
    public required bool HasSent { get; init; }

    /// <summary>True while distribution is in progress — any row still Queued or the run has slips not yet logged (§8 progress bar).</summary>
    public required bool IsSending { get; init; }

    /// <summary>
    /// Per-employee delivery rows for the §8 expandable Sent/Failed/Skipped lists (FR-5). One per logged
    /// employee; employees not yet attempted are reflected only in the queued count, not here.
    /// </summary>
    public required IReadOnlyList<PayslipRecipientStatusDto> Recipients { get; init; }
}

/// <summary>
/// One employee's payslip-email delivery status (US-PAY-011 FR-5, §8 per-row list). Backs the FE's per-row
/// Sent/Failed/Skipped display + per-row "Re-send" action.
/// </summary>
public sealed record PayslipRecipientStatusDto
{
    public required Guid EmployeeId { get; init; }
    public required string EmployeeNo { get; init; }
    public required string EmployeeName { get; init; }
    public required string RecipientEmail { get; init; }
    /// <summary>Queued / Sent / Failed / Skipped (the wire string).</summary>
    public required string Status { get; init; }
    public string? FailureReason { get; init; }
    public DateTime? SentAt { get; init; }
    public required int RetryCount { get; init; }
}
