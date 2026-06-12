using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// Handler for AssignManagerCommand (US-CHR-011).
/// Delegates to IReportingStructureService.
/// </summary>
public sealed class AssignManagerCommandHandler
    : IRequestHandler<AssignManagerCommand, Result<AssignManagerResult>>
{
    private readonly IReportingStructureService _reportingService;

    public AssignManagerCommandHandler(IReportingStructureService reportingService)
    {
        _reportingService = reportingService;
    }

    public async Task<Result<AssignManagerResult>> Handle(
        AssignManagerCommand request,
        CancellationToken cancellationToken)
    {
        var assignRequest = new AssignManagerRequest
        {
            ManagerEmployeeId = request.ManagerEmployeeId,
            Reason = request.Reason,
        };

        return await _reportingService.AssignManagerAsync(
            request.EmployeeId, assignRequest, cancellationToken);
    }
}
