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
