using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using MediatR;

namespace HRM.Application.Features.Departments.Commands;

public sealed class DeactivateDepartmentCommandHandler
    : IRequestHandler<DeactivateDepartmentCommand, Result>
{
    private readonly IDepartmentService _departmentService;

    public DeactivateDepartmentCommandHandler(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    public Task<Result> Handle(
        DeactivateDepartmentCommand request, CancellationToken cancellationToken)
    {
        return _departmentService.DeactivateAsync(request.DepartmentId, cancellationToken);
    }
}
