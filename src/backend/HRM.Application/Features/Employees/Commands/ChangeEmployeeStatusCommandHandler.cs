using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Employees.DTOs;
using MediatR;

namespace HRM.Application.Features.Employees.Commands;

/// <summary>
/// Handler for ChangeEmployeeStatusCommand (US-CHR-009).
/// Delegates to IEmployeeStatusService.
/// </summary>
public sealed class ChangeEmployeeStatusCommandHandler
    : IRequestHandler<ChangeEmployeeStatusCommand, Result<ChangeEmployeeStatusResult>>
{
    private readonly IEmployeeStatusService _statusService;

    public ChangeEmployeeStatusCommandHandler(IEmployeeStatusService statusService)
    {
        _statusService = statusService;
    }

    public async Task<Result<ChangeEmployeeStatusResult>> Handle(
        ChangeEmployeeStatusCommand request,
        CancellationToken cancellationToken)
    {
        var changeRequest = new ChangeEmployeeStatusRequest
        {
            NewStatus = request.NewStatus,
            Reason = request.Reason,
            EffectiveDate = request.EffectiveDate,
        };

        return await _statusService.ChangeStatusAsync(
            request.EmployeeId,
            changeRequest,
            request.IdempotencyKey,
            cancellationToken);
    }
}
