using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;
using MediatR;

namespace HRM.Application.Features.Departments.Queries;

public sealed class GetDepartmentTreeQueryHandler
    : IRequestHandler<GetDepartmentTreeQuery, Result<IReadOnlyList<DepartmentTreeNodeDto>>>
{
    private readonly IDepartmentService _departmentService;

    public GetDepartmentTreeQueryHandler(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    public Task<Result<IReadOnlyList<DepartmentTreeNodeDto>>> Handle(
        GetDepartmentTreeQuery request, CancellationToken cancellationToken)
    {
        return _departmentService.GetTreeAsync(cancellationToken);
    }
}
