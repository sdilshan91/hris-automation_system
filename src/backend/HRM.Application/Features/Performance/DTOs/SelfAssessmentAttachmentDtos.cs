namespace HRM.Application.Features.Performance.DTOs;

/// <summary>
/// Service-layer input for uploading a self-assessment evidence file (US-PRF-002 FR-5 / ISSUE-105). Carries
/// the raw stream + metadata; the owning employee is resolved from the authenticated caller (never supplied),
/// and the storage path is namespaced by tenant + self-assessment so files never leak across tenants (NFR-4).
/// </summary>
public sealed record UploadSelfAssessmentAttachmentInput(
    Guid CycleId,
    Guid GoalId,
    Stream Content,
    string FileName,
    string? ContentType,
    long SizeBytes);

/// <summary>
/// Download payload for a self-assessment evidence file (ISSUE-105): the raw bytes streamed back through the
/// authenticated, tenant-scoped API rather than a direct blob URL (mirrors the recruitment
/// <c>ResumeDownloadDto</c>). Ownership is checked in the service before the bytes are read.
/// </summary>
public sealed record SelfAssessmentAttachmentDownloadDto
{
    public required byte[] Content { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
}
