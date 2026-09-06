using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;
using MediatR;

namespace HRM.Application.Features.LeaveRequests.Commands;

/// <summary>
/// Uploads one virus-scanned supporting document for the calling employee's leave application
/// (US-LV-003 FR-5 / ISSUE-036). The stream + metadata come from the multipart request; the owning employee
/// is resolved from the authenticated caller. The returned attachment id is what the create-leave request
/// then references in <c>AttachmentIds</c>.
/// </summary>
public sealed record UploadLeaveAttachmentCommand(
    Stream Content,
    string FileName,
    string? ContentType,
    long SizeBytes
) : IRequest<Result<LeaveAttachmentDto>>;

public sealed class UploadLeaveAttachmentCommandHandler
    : IRequestHandler<UploadLeaveAttachmentCommand, Result<LeaveAttachmentDto>>
{
    private readonly ILeaveAttachmentService _service;

    public UploadLeaveAttachmentCommandHandler(ILeaveAttachmentService service) => _service = service;

    public Task<Result<LeaveAttachmentDto>> Handle(
        UploadLeaveAttachmentCommand request, CancellationToken cancellationToken)
        => _service.UploadAsync(
            new UploadLeaveAttachmentInput(
                request.Content, request.FileName, request.ContentType, request.SizeBytes),
            cancellationToken);
}
