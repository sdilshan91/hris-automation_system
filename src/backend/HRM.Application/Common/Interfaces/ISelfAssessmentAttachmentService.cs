using HRM.Application.Common.Models;
using HRM.Application.Features.Performance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Self-assessment evidence-file attachments (US-PRF-002 FR-5 / ISSUE-105). Kept SEPARATE from
/// <see cref="ISelfAssessmentService"/> so the ratings service constructor stays unchanged. Every operation
/// is scoped to the CALLING employee's own self-assessment (resolved from <c>ICurrentUser.UserId</c>) AND
/// their tenant (EF global query filter), so an employee can never read or write another employee's evidence
/// (NFR-2). Attachments hang off a <c>SelfAssessmentItem</c> (one goal); the per-goal limit (≤5 files) and
/// size/type caps are enforced here. Uploads are virus-scanned (<c>IVirusScanner</c>) before they are stored
/// (<c>IFileStorage</c>) and persisted (NFR-4).
/// </summary>
public interface ISelfAssessmentAttachmentService
{
    /// <summary>
    /// Virus-scans, stores, and persists an evidence file for one goal of the caller's self-assessment.
    /// Creates the draft self-assessment + goal item on demand if they don't exist yet (mirrors save-draft).
    /// Fails 409 if the window is closed (BR-1) or the assessment is already submitted/locked (BR-3), 409 if
    /// the per-goal file limit is reached, 400 for an oversized/disallowed file, and 400 if the scan is not
    /// clean.
    /// </summary>
    Task<Result<SelfAssessmentAttachmentDto>> UploadAsync(
        UploadSelfAssessmentAttachmentInput input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the evidence-file metadata (never the bytes) attached to one goal of the caller's
    /// self-assessment. Returns an empty list when nothing is attached (or no draft exists yet).
    /// </summary>
    Task<Result<IReadOnlyList<SelfAssessmentAttachmentDto>>> ListAsync(
        Guid cycleId, Guid goalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the raw bytes of one evidence file for streaming download. Tenant-scoped and ownership-checked
    /// (the attachment must belong to the caller's own self-assessment); 404 otherwise.
    /// </summary>
    Task<Result<SelfAssessmentAttachmentDownloadDto>> DownloadAsync(
        Guid attachmentId, CancellationToken cancellationToken = default);
}
