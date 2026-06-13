using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

/// <summary>
/// Creates a new leave entitlement rule (US-LV-002 AC-1).
/// </summary>
public sealed record CreateLeaveEntitlementRuleCommand(
    Guid LeaveTypeId,
    Guid? DepartmentId,
    Guid? JobTitleId,
    Guid? JobLevelId,
    string? EmploymentType,
    int? TenureMinMonths,
    int? TenureMaxMonths,
    decimal EntitlementDays,
    int Priority,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo
) : IRequest<Result<LeaveEntitlementRuleDto>>;
