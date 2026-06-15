using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;
using MediatR;

namespace HRM.Application.Features.Recruitment.Commands;

/// <summary>
/// US-REC-003 (FR-8): moves several applicants to the same stage in one action. Same per-applicant
/// rules as <see cref="MoveApplicantStageCommand"/> (BR-3/BR-4); all-or-nothing so the caller never gets
/// a partial move. CSV export and bulk email (also under FR-8) are out of scope — deferred. Tenant-scoped.
/// </summary>
public sealed record BulkMoveApplicantStageCommand(
    IReadOnlyList<Guid> ApplicantIds,
    ApplicantStage ToStage,
    string? Reason,
    string? Notes
) : IRequest<Result<BulkMoveApplicantStageResultDto>>;

public sealed class BulkMoveApplicantStageCommandHandler
    : IRequestHandler<BulkMoveApplicantStageCommand, Result<BulkMoveApplicantStageResultDto>>
{
    private readonly IApplicantService _service;

    public BulkMoveApplicantStageCommandHandler(IApplicantService service) => _service = service;

    public Task<Result<BulkMoveApplicantStageResultDto>> Handle(BulkMoveApplicantStageCommand request, CancellationToken cancellationToken)
        => _service.BulkMoveStageAsync(request.ApplicantIds, request.ToStage, request.Reason, request.Notes, cancellationToken);
}
