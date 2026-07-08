using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;
using MediatR;

namespace HRM.Application.Features.Performance.Commands;

// ── Upload evidence file (US-PRF-002 FR-5 / ISSUE-105) ─────────────────

/// <summary>
/// Uploads a virus-scanned evidence file for one goal of the caller's own self-assessment. The stream +
/// metadata come from the multipart request; the owning employee is resolved from the authenticated caller.
/// </summary>
public sealed record UploadSelfAssessmentAttachmentCommand(
    Guid CycleId,
    Guid GoalId,
    Stream Content,
    string FileName,
    string? ContentType,
    long SizeBytes
) : IRequest<Result<SelfAssessmentAttachmentDto>>;

public sealed class UploadSelfAssessmentAttachmentCommandHandler
    : IRequestHandler<UploadSelfAssessmentAttachmentCommand, Result<SelfAssessmentAttachmentDto>>
{
    private readonly ISelfAssessmentAttachmentService _service;
    public UploadSelfAssessmentAttachmentCommandHandler(ISelfAssessmentAttachmentService service)
        => _service = service;

    public Task<Result<SelfAssessmentAttachmentDto>> Handle(
        UploadSelfAssessmentAttachmentCommand request, CancellationToken cancellationToken)
        => _service.UploadAsync(
            new UploadSelfAssessmentAttachmentInput(
                request.CycleId, request.GoalId, request.Content,
                request.FileName, request.ContentType, request.SizeBytes),
            cancellationToken);
}

// ── Delete an evidence file (US-PRF-002 FR-5 / ISSUE-105 / BUG-244 #4) ──

/// <summary>
/// Removes one evidence file from the caller's OWN self-assessment. The owning employee is resolved from the
/// authenticated caller; the service verifies the attachment belongs to the named self-assessment + the caller
/// (ownership, NFR-2) and that the self-assessment window is still open + not submitted (BR-1/BR-3) before it
/// deletes the blob and the row.
/// </summary>
public sealed record DeleteSelfAssessmentAttachmentCommand(Guid AssessmentId, Guid AttachmentId)
    : IRequest<Result>;

public sealed class DeleteSelfAssessmentAttachmentCommandHandler
    : IRequestHandler<DeleteSelfAssessmentAttachmentCommand, Result>
{
    private readonly ISelfAssessmentAttachmentService _service;
    public DeleteSelfAssessmentAttachmentCommandHandler(ISelfAssessmentAttachmentService service)
        => _service = service;

    public Task<Result> Handle(
        DeleteSelfAssessmentAttachmentCommand request, CancellationToken cancellationToken)
        => _service.DeleteAsync(request.AssessmentId, request.AttachmentId, cancellationToken);
}
