using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Commands;

/// <summary>Bulk-assigns a shift to employees with an effective date (US-ATT-005 AC-2/AC-3/FR-3).</summary>
public sealed record AssignShiftCommand(
    Guid ShiftId,
    IReadOnlyList<Guid> EmployeeIds,
    DateOnly EffectiveFrom) : IRequest<Result<AssignmentResultDto>>;

public sealed class AssignShiftCommandHandler
    : IRequestHandler<AssignShiftCommand, Result<AssignmentResultDto>>
{
    private readonly IShiftService _shiftService;

    public AssignShiftCommandHandler(IShiftService shiftService) => _shiftService = shiftService;

    public Task<Result<AssignmentResultDto>> Handle(
        AssignShiftCommand request, CancellationToken cancellationToken)
        => _shiftService.AssignAsync(
            request.ShiftId, request.EmployeeIds, request.EffectiveFrom, cancellationToken);
}
