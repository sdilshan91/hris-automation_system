using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveTypes.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveTypes.Commands;

public sealed class CreateLeaveTypeCommandHandler
    : IRequestHandler<CreateLeaveTypeCommand, Result<LeaveTypeDto>>
{
    private readonly ILeaveTypeService _leaveTypeService;

    public CreateLeaveTypeCommandHandler(ILeaveTypeService leaveTypeService)
    {
        _leaveTypeService = leaveTypeService;
    }

    public Task<Result<LeaveTypeDto>> Handle(
        CreateLeaveTypeCommand request, CancellationToken cancellationToken)
    {
        var createRequest = new CreateLeaveTypeRequest
        {
            Name = request.Name,
            Code = request.Code,
            Color = request.Color,
            Description = request.Description,
            AnnualEntitlement = request.AnnualEntitlement,
            AccrualFrequency = request.AccrualFrequency,
            CarryForwardLimit = request.CarryForwardLimit,
            CarryForwardExpiryMonths = request.CarryForwardExpiryMonths,
            ProbationEligible = request.ProbationEligible,
            DocumentsRequired = request.DocumentsRequired,
            DocumentDayThreshold = request.DocumentDayThreshold,
            Encashable = request.Encashable,
            MaxEncashDays = request.MaxEncashDays,
            HalfDayAllowed = request.HalfDayAllowed,
            HourlyAllowed = request.HourlyAllowed,
            Gender = request.Gender,
            MaxConsecutiveDays = request.MaxConsecutiveDays,
            NegativeBalanceAllowed = request.NegativeBalanceAllowed,
            NegativeBalanceLimit = request.NegativeBalanceLimit,
            DisplayOrder = request.DisplayOrder,
        };

        return _leaveTypeService.CreateAsync(createRequest, cancellationToken);
    }
}
