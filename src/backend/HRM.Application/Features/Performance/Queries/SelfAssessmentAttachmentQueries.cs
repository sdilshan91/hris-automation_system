using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Queries;

// ── List evidence files for a goal (US-PRF-002 FR-5 / ISSUE-105) ───────

/// <summary>
/// Lists the evidence-file metadata (no bytes) attached to one goal of the caller's own self-assessment.
/// </summary>
public sealed record ListSelfAssessmentAttachmentsQuery(Guid CycleId, Guid GoalId)
    : IRequest<Result<IReadOnlyList<SelfAssessmentAttachmentDto>>>;

public sealed class ListSelfAssessmentAttachmentsQueryHandler
    : IRequestHandler<ListSelfAssessmentAttachmentsQuery, Result<IReadOnlyList<SelfAssessmentAttachmentDto>>>
{
    private readonly ISelfAssessmentAttachmentService _service;
    public ListSelfAssessmentAttachmentsQueryHandler(ISelfAssessmentAttachmentService service)
        => _service = service;

    public Task<Result<IReadOnlyList<SelfAssessmentAttachmentDto>>> Handle(
        ListSelfAssessmentAttachmentsQuery request, CancellationToken cancellationToken)
        => _service.ListAsync(request.CycleId, request.GoalId, cancellationToken);
}

// ── Download one evidence file ─────────────────────────────────────────

/// <summary>
/// Streams the raw bytes of one evidence file (tenant-scoped + ownership-checked). 404 if the attachment does
/// not belong to the caller's own self-assessment or the file is gone.
/// </summary>
public sealed record DownloadSelfAssessmentAttachmentQuery(Guid AttachmentId)
    : IRequest<Result<SelfAssessmentAttachmentDownloadDto>>;

public sealed class DownloadSelfAssessmentAttachmentQueryHandler
    : IRequestHandler<DownloadSelfAssessmentAttachmentQuery, Result<SelfAssessmentAttachmentDownloadDto>>
{
    private readonly ISelfAssessmentAttachmentService _service;
    public DownloadSelfAssessmentAttachmentQueryHandler(ISelfAssessmentAttachmentService service)
        => _service = service;

    public Task<Result<SelfAssessmentAttachmentDownloadDto>> Handle(
        DownloadSelfAssessmentAttachmentQuery request, CancellationToken cancellationToken)
        => _service.DownloadAsync(request.AttachmentId, cancellationToken);
}
