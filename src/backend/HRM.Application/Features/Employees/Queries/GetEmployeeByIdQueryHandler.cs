using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

public sealed class GetEmployeeByIdQueryHandler
    : IRequestHandler<GetEmployeeByIdQuery, Result<EmployeeDto>>
{
    private readonly IEmployeeService _employeeService;

    public GetEmployeeByIdQueryHandler(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    public Task<Result<EmployeeDto>> Handle(
        GetEmployeeByIdQuery request, CancellationToken cancellationToken)
    {
        return _employeeService.GetByIdAsync(request.EmployeeId, cancellationToken);
    }
}
