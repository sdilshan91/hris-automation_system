namespace HRM.Application.Features.LeaveRequests.DTOs;

/// <summary>
/// Service-layer input for a leave supporting-document upload (US-LV-003 FR-5 / ISSUE-036).
/// The stream + metadata come from the multipart request; the owning employee is resolved from the
/// authenticated caller, never taken from the client.
/// </summary>
public sealed record UploadLeaveAttachmentInput(
    Stream Content,
    string FileName,
    string? ContentType,
    long SizeBytes);

/// <summary>
/// Metadata for one uploaded leave attachment (never the bytes). The <see cref="Id"/> is what the employee
/// then submits in <c>CreateLeaveRequestRequest.AttachmentIds</c> — replacing the pre-ISSUE-036 contract
/// where an unverified file-name string counted as a medical certificate.
/// </summary>
public sealed record LeaveAttachmentDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTime UploadedAt { get; init; }
}
