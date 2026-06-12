using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

public sealed class GetEmployeeProfileQueryHandler
    : IRequestHandler<GetEmployeeProfileQuery, Result<EmployeeProfileDto>>
{
    private readonly IEmployeeService _employeeService;

    public GetEmployeeProfileQueryHandler(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    public Task<Result<EmployeeProfileDto>> Handle(
        GetEmployeeProfileQuery request, CancellationToken cancellationToken)
    {
        return _employeeService.GetProfileAsync(request.EmployeeId, cancellationToken);
    }
}
