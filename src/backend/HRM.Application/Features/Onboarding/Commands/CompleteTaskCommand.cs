using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.DTOs;
using MediatR;

namespace HRM.Application.Features.Onboarding.Commands;

/// <summary>
/// US-ONB-003 AC-3/AC-4/FR-2/FR-3/FR-7: the logged-in employee marks one of their own onboarding tasks
/// completed, optionally with a comment and a required document. The attachment stream (when supplied) is
/// owned by the caller. Business rules (own-task only / responsible_role = Employee, cannot revert, document
/// required, MIME + size) are enforced in the service; structural rules in the validator.
/// </summary>
public sealed record CompleteTaskCommand(
    Guid TaskInstanceId,
    string? Comment,
    Stream? AttachmentStream,
    string? AttachmentFileName,
    string? AttachmentContentType,
    long AttachmentSize
) : IRequest<Result<CompleteTaskResultDto>>;

public sealed class CompleteTaskCommandHandler
    : IRequestHandler<CompleteTaskCommand, Result<CompleteTaskResultDto>>
{
    private readonly IOnboardingChecklistService _service;

    public CompleteTaskCommandHandler(IOnboardingChecklistService service) => _service = service;

    public Task<Result<CompleteTaskResultDto>> Handle(
        CompleteTaskCommand request, CancellationToken cancellationToken)
        => _service.CompleteTaskAsync(new CompleteTaskInput(
            request.TaskInstanceId,
            request.Comment,
            request.AttachmentStream,
            request.AttachmentFileName,
            request.AttachmentContentType,
            request.AttachmentSize),
            cancellationToken);
}
