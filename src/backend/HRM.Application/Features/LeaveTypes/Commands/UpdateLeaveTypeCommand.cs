using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveTypes.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Commands;

/// <summary>
/// Updates an existing leave type (US-LV-001 AC-2).
/// </summary>
public sealed record UpdateLeaveTypeCommand(
    Guid LeaveTypeId,
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
    decimal? NegativeBalanceLimit
) : IRequest<Result<LeaveTypeDto>>;
