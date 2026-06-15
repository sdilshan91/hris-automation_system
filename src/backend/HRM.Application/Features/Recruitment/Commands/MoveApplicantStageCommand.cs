using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-003 (AC-2/FR-3/BR-3/BR-4/BR-5/BR-6): moves a single applicant to a new pipeline stage,
/// persisting the change, writing an immutable stage-history row and an audit-log entry. Reason is
/// mandatory when moving to Rejected (BR-3) or backward (BR-4). Tenant-scoped.
/// </summary>
public sealed record MoveApplicantStageCommand(
    Guid ApplicantId,
    ApplicantStage ToStage,
    string? Reason,
    string? Notes,
    RejectionReason? RejectionReason = null
) : IRequest<Result<MoveApplicantStageResultDto>>;

public sealed class MoveApplicantStageCommandHandler
    : IRequestHandler<MoveApplicantStageCommand, Result<MoveApplicantStageResultDto>>
{
    private readonly IApplicantService _service;

    public MoveApplicantStageCommandHandler(IApplicantService service) => _service = service;

    public Task<Result<MoveApplicantStageResultDto>> Handle(MoveApplicantStageCommand request, CancellationToken cancellationToken)
        => _service.MoveStageAsync(request.ApplicantId, request.ToStage, request.Reason, request.Notes, request.RejectionReason, cancellationToken);
}
