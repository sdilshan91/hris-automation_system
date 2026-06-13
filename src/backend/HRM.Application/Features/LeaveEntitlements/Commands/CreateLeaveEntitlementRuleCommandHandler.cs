using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveEntitlements.Commands;

public sealed class CreateLeaveEntitlementRuleCommandHandler
    : IRequestHandler<CreateLeaveEntitlementRuleCommand, Result<LeaveEntitlementRuleDto>>
{
    private readonly ILeaveEntitlementService _service;

    public CreateLeaveEntitlementRuleCommandHandler(ILeaveEntitlementService service)
    {
        _service = service;
    }

    public Task<Result<LeaveEntitlementRuleDto>> Handle(
        CreateLeaveEntitlementRuleCommand request, CancellationToken cancellationToken)
    {
        return _service.CreateRuleAsync(new UpsertLeaveEntitlementRuleRequest
        {
            LeaveTypeId = request.LeaveTypeId,
            DepartmentId = request.DepartmentId,
            JobTitleId = request.JobTitleId,
            JobLevelId = request.JobLevelId,
            EmploymentType = request.EmploymentType,
            TenureMinMonths = request.TenureMinMonths,
            TenureMaxMonths = request.TenureMaxMonths,
            EntitlementDays = request.EntitlementDays,
            Priority = request.Priority,
            EffectiveFrom = request.EffectiveFrom,
            EffectiveTo = request.EffectiveTo,
        }, cancellationToken);
    }
}
