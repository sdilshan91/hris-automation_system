using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>
/// ISSUE-077 (US-ATT-005 FR-5/BR-1): sets a shift as the tenant default working calendar, transferring
/// the <c>is_default</c> flag so exactly one shift is the default at any time.
/// </summary>
public sealed record SetDefaultShiftCommand(Guid ShiftId) : IRequest<Result<ShiftDto>>;

public sealed class SetDefaultShiftCommandHandler : IRequestHandler<SetDefaultShiftCommand, Result<ShiftDto>>
{
    private readonly IShiftService _shiftService;

    public SetDefaultShiftCommandHandler(IShiftService shiftService) => _shiftService = shiftService;

    public Task<Result<ShiftDto>> Handle(SetDefaultShiftCommand request, CancellationToken cancellationToken)
        => _shiftService.SetDefaultAsync(request.ShiftId, cancellationToken);
}
