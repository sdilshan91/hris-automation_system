using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// Handler for BulkAssignManagerCommand (US-CHR-011 FR-4, AC-5).
/// Delegates to IReportingStructureService.
/// </summary>
public sealed class BulkAssignManagerCommandHandler
    : IRequestHandler<BulkAssignManagerCommand, Result<BulkAssignManagerResult>>
{
    private readonly IReportingStructureService _reportingService;

    public BulkAssignManagerCommandHandler(IReportingStructureService reportingService)
    {
        _reportingService = reportingService;
    }

    public async Task<Result<BulkAssignManagerResult>> Handle(
        BulkAssignManagerCommand request,
        CancellationToken cancellationToken)
    {
        var bulkRequest = new BulkAssignManagerRequest
        {
            EmployeeIds = request.EmployeeIds,
            ManagerEmployeeId = request.ManagerEmployeeId,
            Reason = request.Reason,
        };

        return await _reportingService.BulkAssignManagerAsync(bulkRequest, cancellationToken);
    }
}
