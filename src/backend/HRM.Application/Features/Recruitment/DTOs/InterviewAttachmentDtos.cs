using HRM.Domain.Enums;

namespace HRM.Application.Features.Recruitment.DTOs;

/// <summary>One document attached to an interview (US-REC-005 FR-8). camelCase on the wire.</summary>
public sealed record InterviewAttachmentDto
{
    public Guid Id { get; init; }
    public Guid InterviewId { get; init; }
    public string FileName { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public string MimeType { get; init; } = string.Empty;
    public InterviewAttachmentKind Kind { get; init; }
    public string KindName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public Guid UploadedBy { get; init; }
    public DateTime UploadedAt { get; init; }
}

/// <summary>The bytes of an attachment plus what the browser needs to render them.</summary>
public sealed record InterviewAttachmentContentDto(byte[] Content, string ContentType, string FileName);
