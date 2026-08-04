using HRM.Application.Common.Models;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// US-REC-005 FR-8: attach an interview guide or evaluation-criteria document to an interview.
///
/// <para>Separate from <c>IInterviewService</c> because it needs the file stack (storage, virus scanner,
/// signature validation) that scheduling has no use for — the same split <c>EmployeeDocumentService</c> and
/// <c>SelfAssessmentAttachmentService</c> already follow.</para>
/// </summary>
public interface IInterviewAttachmentService
{
    /// <summary>
    /// Uploads a document against an interview. Validates the real magic bytes (never the client-supplied
    /// content type), virus-scans before persistence, and stores under a tenant-isolated path.
    /// </summary>
    Task<Result<InterviewAttachmentDto>> UploadAsync(
        Guid interviewId,
        Stream content,
        string fileName,
        string contentType,
        long fileSize,
        InterviewAttachmentKind kind,
        string? description,
        CancellationToken cancellationToken = default);

    /// <summary>Lists an interview's attachments, newest first.</summary>
    Task<Result<IReadOnlyList<InterviewAttachmentDto>>> ListAsync(
        Guid interviewId, CancellationToken cancellationToken = default);

    /// <summary>Downloads one attachment's bytes.</summary>
    Task<Result<InterviewAttachmentContentDto>> DownloadAsync(
        Guid attachmentId, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes an attachment. The stored blob is left for the retention sweep, as elsewhere.</summary>
    Task<Result> DeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}
