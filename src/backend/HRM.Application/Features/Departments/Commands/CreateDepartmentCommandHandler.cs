using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;
using MediatR;

namespace HRM.Application.Features.Departments.Commands;

public sealed class CreateDepartmentCommandHandler
    : IRequestHandler<CreateDepartmentCommand, Result<DepartmentDto>>
{
    private readonly IDepartmentService _departmentService;

    public CreateDepartmentCommandHandler(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    public Task<Result<DepartmentDto>> Handle(
        CreateDepartmentCommand request, CancellationToken cancellationToken)
    {
        return _departmentService.CreateAsync(
            request.Name, request.Code, request.Description,
            request.ParentDepartmentId, request.ManagerId,
            cancellationToken);
    }
}
