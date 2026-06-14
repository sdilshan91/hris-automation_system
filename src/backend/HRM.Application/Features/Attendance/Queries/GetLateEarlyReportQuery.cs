using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using MediatR;

namespace HRM.Application.Features.Attendance.Queries;

/// <summary>
/// US-ATT-008 AC-5/FR-6: late/early-departure report for a date range. scope="team" → the acting
/// manager's direct reports; scope="all" → every employee (HR only). Optional department/employee filters.
/// </summary>
public sealed record GetLateEarlyReportQuery(
    DateOnly From, DateOnly To, Guid? DepartmentId, Guid? EmployeeId, string Scope)
    : IRequest<Result<LateEarlyReportResult>>;

public sealed class GetLateEarlyReportQueryHandler
    : IRequestHandler<GetLateEarlyReportQuery, Result<LateEarlyReportResult>>
{
    private readonly ILateEarlyService _service;

    public GetLateEarlyReportQueryHandler(ILateEarlyService service)
    {
        _service = service;
    }

    public Task<Result<LateEarlyReportResult>> Handle(
        GetLateEarlyReportQuery request, CancellationToken cancellationToken)
        => _service.GetReportAsync(
            request.From, request.To, request.DepartmentId, request.EmployeeId, request.Scope, cancellationToken);
}
