using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

public sealed class CreateEmployeeCommandHandler
    : IRequestHandler<CreateEmployeeCommand, Result<EmployeeDto>>
{
    private readonly IEmployeeService _employeeService;

    public CreateEmployeeCommandHandler(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    public Task<Result<EmployeeDto>> Handle(
        CreateEmployeeCommand request, CancellationToken cancellationToken)
    {
        var createRequest = new CreateEmployeeRequest
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            DateOfBirth = request.DateOfBirth,
            Gender = request.Gender,
            DateOfJoining = request.DateOfJoining,
            DepartmentId = request.DepartmentId,
            JobTitleId = request.JobTitleId,
            EmploymentType = request.EmploymentType,
            Status = request.Status,
            Location = request.Location,
            CustomFields = request.CustomFields,
            UserId = request.UserId,
        };

        return _employeeService.CreateAsync(createRequest, cancellationToken);
    }
}
