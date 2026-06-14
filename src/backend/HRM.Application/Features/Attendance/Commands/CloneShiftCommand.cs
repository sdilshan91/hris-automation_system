using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>Clones a shift to create a variant (US-ATT-005 FR-8).</summary>
public sealed record CloneShiftCommand(Guid ShiftId) : IRequest<Result<ShiftDto>>;

public sealed class CloneShiftCommandHandler : IRequestHandler<CloneShiftCommand, Result<ShiftDto>>
{
    private readonly IShiftService _shiftService;

    public CloneShiftCommandHandler(IShiftService shiftService) => _shiftService = shiftService;

    public Task<Result<ShiftDto>> Handle(CloneShiftCommand request, CancellationToken cancellationToken)
        => _shiftService.CloneAsync(request.ShiftId, cancellationToken);
}
