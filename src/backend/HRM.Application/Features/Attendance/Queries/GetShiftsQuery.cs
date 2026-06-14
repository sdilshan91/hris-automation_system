using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>Lists all shifts for the current tenant with assigned-employee counts (US-ATT-005).</summary>
public sealed record GetShiftsQuery : IRequest<Result<IReadOnlyList<ShiftDto>>>;

public sealed class GetShiftsQueryHandler
    : IRequestHandler<GetShiftsQuery, Result<IReadOnlyList<ShiftDto>>>
{
    private readonly IShiftService _shiftService;

    public GetShiftsQueryHandler(IShiftService shiftService) => _shiftService = shiftService;

    public Task<Result<IReadOnlyList<ShiftDto>>> Handle(
        GetShiftsQuery request, CancellationToken cancellationToken)
        => _shiftService.GetAllAsync(cancellationToken);
}
