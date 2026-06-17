using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>
/// US-ONB-006 AC-2/FR-3/FR-8: records an exit interview. Persists the responses, links to the offboarding,
/// completes the exit-interview offboarding task, and (self-service) writes an HR-notification outbox row.
/// BR-1: a duplicate (existing active interview) is rejected unless <see cref="AllowEdit"/> (detail-permitted
/// HR re-version, BR-2). <see cref="IsSelfService"/> drives the BR-3 deadline + the self-service notification
/// and scopes a self-service caller to their own offboarding (resolved from the session in the service).
/// </summary>
public sealed record RecordExitInterviewCommand(
    RecordExitInterviewInput Input,
    bool IsSelfService,
    bool AllowEdit
) : IRequest<Result<ExitInterviewDto>>;

public sealed class RecordExitInterviewCommandHandler
    : IRequestHandler<RecordExitInterviewCommand, Result<ExitInterviewDto>>
{
    private readonly IExitInterviewService _service;

    public RecordExitInterviewCommandHandler(IExitInterviewService service) => _service = service;

    public Task<Result<ExitInterviewDto>> Handle(
        RecordExitInterviewCommand request, CancellationToken cancellationToken)
        => _service.RecordAsync(
            request.Input,
            request.IsSelfService,
            request.AllowEdit,
            cancellationToken);
}
