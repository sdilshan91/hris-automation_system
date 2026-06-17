using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>US-ONB-005 AC-3: records a department clearance decision on an offboarding task.</summary>
public sealed record RecordClearanceCommand(
    Guid TaskId,
    ClearanceStatus Status,
    string? Remarks
) : IRequest<Result<OffboardingInstanceDto>>;

public sealed class RecordClearanceCommandHandler
    : IRequestHandler<RecordClearanceCommand, Result<OffboardingInstanceDto>>
{
    private readonly IOffboardingService _service;

    public RecordClearanceCommandHandler(IOffboardingService service) => _service = service;

    public Task<Result<OffboardingInstanceDto>> Handle(
        RecordClearanceCommand request, CancellationToken cancellationToken)
        => _service.RecordClearanceAsync(
            new RecordClearanceInput(request.TaskId, request.Status, request.Remarks), cancellationToken);
}
