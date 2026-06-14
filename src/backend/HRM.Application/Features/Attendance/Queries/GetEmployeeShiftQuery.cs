using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// Resolves the shift effective for an employee on a date, resolving rotation and falling back to the
/// tenant default (US-ATT-005 FR-5/FR-7).
/// </summary>
public sealed record GetEmployeeShiftQuery(Guid EmployeeId, DateOnly Date)
    : IRequest<Result<ResolvedShiftDto>>;

public sealed class GetEmployeeShiftQueryHandler
    : IRequestHandler<GetEmployeeShiftQuery, Result<ResolvedShiftDto>>
{
    private readonly IShiftService _shiftService;

    public GetEmployeeShiftQueryHandler(IShiftService shiftService) => _shiftService = shiftService;

    public Task<Result<ResolvedShiftDto>> Handle(
        GetEmployeeShiftQuery request, CancellationToken cancellationToken)
        => _shiftService.ResolveForEmployeeAsync(request.EmployeeId, request.Date, cancellationToken);
}
