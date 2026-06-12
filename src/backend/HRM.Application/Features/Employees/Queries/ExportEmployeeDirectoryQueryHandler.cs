using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Queries;

public sealed class ExportEmployeeDirectoryQueryHandler
    : IRequestHandler<ExportEmployeeDirectoryQuery, Result<ExportFileResult>>
{
    private readonly IEmployeeDirectoryService _directoryService;
    private readonly ICurrentUser _currentUser;

    public ExportEmployeeDirectoryQueryHandler(
        IEmployeeDirectoryService directoryService,
        ICurrentUser currentUser)
    {
        _directoryService = directoryService;
        _currentUser = currentUser;
    }

    public async Task<Result<ExportFileResult>> Handle(
        ExportEmployeeDirectoryQuery request, CancellationToken cancellationToken)
    {
        var visibility = ResolveVisibility();
        var showArchived = request.ShowArchived && visibility == DirectoryFieldVisibility.Full;

        return await _directoryService.ExportDirectoryAsync(
            request.Format,
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

        return DirectoryFieldVisibility.Basic;
    }
}
