using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

public sealed class GetEmployeeDirectoryQueryHandler
    : IRequestHandler<GetEmployeeDirectoryQuery, Result<EmployeeDirectoryResult>>
{
    private readonly IEmployeeDirectoryService _directoryService;
    private readonly ICurrentUser _currentUser;

    public GetEmployeeDirectoryQueryHandler(
        IEmployeeDirectoryService directoryService,
        ICurrentUser currentUser)
    {
        _directoryService = directoryService;
        _currentUser = currentUser;
    }

    public Task<Result<EmployeeDirectoryResult>> Handle(
        GetEmployeeDirectoryQuery request, CancellationToken cancellationToken)
    {
        // Determine field visibility from caller's role/permissions
        var visibility = ResolveVisibility();

        // Only HR Officers (Employee.View.All) may use ShowArchived (BR-1)
        var showArchived = request.ShowArchived && visibility == DirectoryFieldVisibility.Full;

        return _directoryService.GetDirectoryAsync(
            request.Search,
            request.DepartmentIds,
            request.JobTitles,
            request.Statuses,
            request.EmploymentTypes,
            request.Locations,
            request.DateOfJoiningFrom,
            request.DateOfJoiningTo,
            request.SortBy,
            request.SortDescending,
            request.Page,
            request.PageSize,
            showArchived,
            visibility,
            cancellationToken);
    }

    private DirectoryFieldVisibility ResolveVisibility()
    {
        var permissions = _currentUser.Permissions;

        if (permissions.Contains("Employee.View.All"))
            return DirectoryFieldVisibility.Full;

        if (permissions.Contains("Employee.View.Team"))
            return DirectoryFieldVisibility.Manager;

        // Employee.View.Own or any authenticated user with directory access
        return DirectoryFieldVisibility.Basic;
    }
}
