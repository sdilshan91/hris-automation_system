using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>
/// US-ONB-005 AC-4/AC-5/FR-5/FR-6/FR-7: completes the offboarding. Blocks (with the pending list) when a
/// mandatory clearance/task is outstanding; on success deactivates the account, revokes sessions, and
/// triggers F&amp;F to Payroll. Irreversible (BR-6).
/// </summary>
public sealed record CompleteOffboardingCommand(Guid OffboardingInstanceId)
    : IRequest<Result<CompleteOffboardingResultDto>>;

public sealed class CompleteOffboardingCommandHandler
    : IRequestHandler<CompleteOffboardingCommand, Result<CompleteOffboardingResultDto>>
{
    private readonly IOffboardingService _service;

    public CompleteOffboardingCommandHandler(IOffboardingService service) => _service = service;

    public Task<Result<CompleteOffboardingResultDto>> Handle(
        CompleteOffboardingCommand request, CancellationToken cancellationToken)
        => _service.CompleteAsync(request.OffboardingInstanceId, cancellationToken);
}
