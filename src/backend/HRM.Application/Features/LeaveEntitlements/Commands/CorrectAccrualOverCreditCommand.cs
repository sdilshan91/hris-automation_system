using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

/// <summary>
/// BUG-291 remediation — writes corrective negative <c>Adjusted</c> ledger entries for employees still holding
/// a legacy over-credited leave balance (a Monthly/Quarterly type that credited a full year on day one, before
/// the 2026-07-30 accrual fix).
///
/// <para><b><see cref="DryRun"/> defaults to true and the endpoint requires it to be explicitly set false.</b>
/// Reducing a visible leave balance is an employee-detriment change: staff can see those days, may have
/// planned around them, and in several jurisdictions accrued leave is a contractual entitlement. Engineering
/// supplies the mechanism; running it for real is a business decision.</para>
/// </summary>
public sealed record CorrectAccrualOverCreditCommand(
    DateOnly AsOfDate,
    bool DryRun
) : IRequest<Result<AccrualOverCreditCorrectionResultDto>>;
