using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

public sealed class RevealNationalIdQueryHandler
    : IRequestHandler<RevealNationalIdQuery, Result<NationalIdRevealDto>>
{
    private readonly IEmployeeService _employeeService;

    public RevealNationalIdQueryHandler(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    public Task<Result<NationalIdRevealDto>> Handle(
        RevealNationalIdQuery request, CancellationToken cancellationToken)
    {
        return _employeeService.RevealNationalIdAsync(request.EmployeeId, cancellationToken);
    }
}
