using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>Deletes a shift unless it has active assignments (US-ATT-005 AC-4/FR-6).</summary>
public sealed record DeleteShiftCommand(Guid ShiftId) : IRequest<Result>;

public sealed class DeleteShiftCommandHandler : IRequestHandler<DeleteShiftCommand, Result>
{
    private readonly IShiftService _shiftService;

    public DeleteShiftCommandHandler(IShiftService shiftService) => _shiftService = shiftService;

    public Task<Result> Handle(DeleteShiftCommand request, CancellationToken cancellationToken)
        => _shiftService.DeleteAsync(request.ShiftId, cancellationToken);
}
