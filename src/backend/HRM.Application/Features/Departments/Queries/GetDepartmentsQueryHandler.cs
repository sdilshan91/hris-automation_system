using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Departments.DTOs;
using MediatR;

namespace HRM.Application.Features.Departments.Queries;

public sealed class GetDepartmentsQueryHandler
    : IRequestHandler<GetDepartmentsQuery, Result<IReadOnlyList<DepartmentDto>>>
{
    private readonly IDepartmentService _departmentService;

    public GetDepartmentsQueryHandler(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    public Task<Result<IReadOnlyList<DepartmentDto>>> Handle(
        GetDepartmentsQuery request, CancellationToken cancellationToken)
    {
        return _departmentService.GetAllAsync(request.ActiveOnly, cancellationToken);
    }
}
