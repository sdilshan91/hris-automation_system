using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>Updates a shift definition (US-ATT-005 FR-2).</summary>
public sealed record UpdateShiftCommand(Guid ShiftId, ShiftRequest Request) : IRequest<Result<ShiftDto>>;

public sealed class UpdateShiftCommandHandler : IRequestHandler<UpdateShiftCommand, Result<ShiftDto>>
{
    private readonly IShiftService _shiftService;

    public UpdateShiftCommandHandler(IShiftService shiftService) => _shiftService = shiftService;

    public Task<Result<ShiftDto>> Handle(UpdateShiftCommand request, CancellationToken cancellationToken)
        => _shiftService.UpdateAsync(request.ShiftId, request.Request, cancellationToken);
}
