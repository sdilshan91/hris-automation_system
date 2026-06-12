using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

/// <summary>
/// Handler for GetValidStatusTransitionsQuery (US-CHR-009 FR-2).
/// </summary>
public sealed class GetValidStatusTransitionsQueryHandler
    : IRequestHandler<GetValidStatusTransitionsQuery, Result<ValidTransitionsResult>>
{
    private readonly IEmployeeStatusService _statusService;

    public GetValidStatusTransitionsQueryHandler(IEmployeeStatusService statusService)
    {
        _statusService = statusService;
    }

    public Task<Result<ValidTransitionsResult>> Handle(
        GetValidStatusTransitionsQuery request,
        CancellationToken cancellationToken)
    {
        return _statusService.GetValidTransitionsAsync(request.EmployeeId, cancellationToken);
    }
}
