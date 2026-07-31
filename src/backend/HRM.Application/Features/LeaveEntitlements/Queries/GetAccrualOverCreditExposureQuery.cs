using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Queries;

/// <summary>
/// BUG-291 exposure report (READ-ONLY): lists, for the current tenant, every (employee × leave type) whose
/// legacy full-year accrual over-credited relative to its configured accrual frequency as of
/// <paramref name="AsOfDate"/>. Changes nothing — reports the affected population only.
/// </summary>
public sealed record GetAccrualOverCreditExposureQuery(
    DateOnly AsOfDate
) : IRequest<Result<AccrualOverCreditExposureReportDto>>;
