using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;
using MediatR;

namespace HRM.Application.Features.Departments.Commands;

public sealed class UpdateDepartmentCommandHandler
    : IRequestHandler<UpdateDepartmentCommand, Result<DepartmentDto>>
{
    private readonly IDepartmentService _departmentService;

    public UpdateDepartmentCommandHandler(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    public Task<Result<DepartmentDto>> Handle(
        UpdateDepartmentCommand request, CancellationToken cancellationToken)
    {
        return _departmentService.UpdateAsync(
            request.DepartmentId, request.Name, request.Code, request.Description,
            request.ParentDepartmentId, request.ManagerId,
            cancellationToken);
    }
}
