using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

public sealed class GetEmployeesQueryHandler
    : IRequestHandler<GetEmployeesQuery, Result<EmployeeListResult>>
{
    private readonly IEmployeeService _employeeService;

    public GetEmployeesQueryHandler(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    public Task<Result<EmployeeListResult>> Handle(
        GetEmployeesQuery request, CancellationToken cancellationToken)
    {
        return _employeeService.GetAllAsync(
            request.Page, request.PageSize, request.ActiveOnly, request.Search,
            cancellationToken);
    }
}
