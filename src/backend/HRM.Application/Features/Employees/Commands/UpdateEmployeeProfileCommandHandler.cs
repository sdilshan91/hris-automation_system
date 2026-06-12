using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

public sealed class UpdateEmployeeProfileCommandHandler
    : IRequestHandler<UpdateEmployeeProfileCommand, Result<EmployeeProfileDto>>
{
    private readonly IEmployeeService _employeeService;

    public UpdateEmployeeProfileCommandHandler(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    public Task<Result<EmployeeProfileDto>> Handle(
        UpdateEmployeeProfileCommand request, CancellationToken cancellationToken)
    {
        return _employeeService.UpdateProfileAsync(
            request.EmployeeId, request.Request, cancellationToken);
    }
}
