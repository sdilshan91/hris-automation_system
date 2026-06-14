using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>Creates a shift definition for the current tenant (US-ATT-005 AC-1/FR-1/FR-2).</summary>
public sealed record CreateShiftCommand(ShiftRequest Request) : IRequest<Result<ShiftDto>>;

public sealed class CreateShiftCommandHandler : IRequestHandler<CreateShiftCommand, Result<ShiftDto>>
{
    private readonly IShiftService _shiftService;

    public CreateShiftCommandHandler(IShiftService shiftService) => _shiftService = shiftService;

    public Task<Result<ShiftDto>> Handle(CreateShiftCommand request, CancellationToken cancellationToken)
        => _shiftService.CreateAsync(request.Request, cancellationToken);
}
