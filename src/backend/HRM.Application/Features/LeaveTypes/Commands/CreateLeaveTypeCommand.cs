using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveTypes.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Commands;

/// <summary>
/// Creates a new leave type in the current tenant (US-LV-001 AC-1).
/// </summary>
public sealed record CreateLeaveTypeCommand(
    string Name,
    string? Code,
    string? Color,
    string? Description,
    decimal AnnualEntitlement,
    string AccrualFrequency,
    decimal? CarryForwardLimit,
    int? CarryForwardExpiryMonths,
    bool ProbationEligible,
    bool DocumentsRequired,
    int? DocumentDayThreshold,
    bool Encashable,
    decimal? MaxEncashDays,
    bool HalfDayAllowed,
    bool HourlyAllowed,
    string Gender,
    int? MaxConsecutiveDays,
    bool NegativeBalanceAllowed,
    decimal? NegativeBalanceLimit,
    int? DisplayOrder
) : IRequest<Result<LeaveTypeDto>>;
