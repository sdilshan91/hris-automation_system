using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;
using MediatR;

namespace HRM.Application.Features.Departments.Queries;

public sealed class GetDepartmentByIdQueryHandler
    : IRequestHandler<GetDepartmentByIdQuery, Result<DepartmentDto>>
{
    private readonly IDepartmentService _departmentService;

    public GetDepartmentByIdQueryHandler(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    public Task<Result<DepartmentDto>> Handle(
        GetDepartmentByIdQuery request, CancellationToken cancellationToken)
    {
        return _departmentService.GetByIdAsync(request.DepartmentId, cancellationToken);
    }
}
