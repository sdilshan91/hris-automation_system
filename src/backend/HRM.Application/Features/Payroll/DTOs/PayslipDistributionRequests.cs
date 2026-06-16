namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>
/// Body for the send-payslip-emails request (US-PAY-011 AC-1/FR-7). <c>confirm</c> is the duplicate-send
/// confirmation (BR-5): required to re-send a run whose payslips were already emailed.
/// </summary>
public sealed record SendPayslipEmailsRequest
{
    /// <summary>FR-7/BR-5: confirm re-sending payslips that were already sent for this run.</summary>
    public bool Confirm { get; init; }
}

/// <summary>
/// Body for the selective re-send request (US-PAY-011 FR-4). Provide <c>employeeIds</c> to target specific
/// employees, or set <c>onlyFailed</c> to re-send to everyone whose last delivery for the run was Failed.
/// </summary>
public sealed record ResendPayslipEmailsRequest
{
    /// <summary>Specific employees to re-send to (FR-4). When non-empty, takes precedence over <c>onlyFailed</c>.</summary>
    public IReadOnlyCollection<Guid>? EmployeeIds { get; init; }

    /// <summary>Re-send to all employees whose last delivery was Failed (FR-4).</summary>
    public bool OnlyFailed { get; init; }
}
