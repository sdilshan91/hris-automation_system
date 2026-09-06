using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveRequests.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Real leave supporting-document uploads (US-LV-003 FR-5 / NFR-3 / ISSUE-036).
///
/// <para>Kept SEPARATE from <see cref="ILeaveRequestService"/> so that service's already-long constructor is
/// untouched. The upload happens BEFORE the leave request exists: the returned attachment id is submitted in
/// <c>CreateLeaveRequestRequest.AttachmentIds</c>, and the create path links the row (verifying tenant,
/// ownership, and that the row is not already claimed by another request).</para>
///
/// Ordering mirrors the self-assessment/employee-document paths: validate size + declared MIME → sniff the
/// real magic bytes (<c>FileSignatureValidator</c>) → virus-scan (<c>IVirusScanner</c>) → store
/// (<c>IFileStorage</c>, tenant-scoped path) → persist the row.
/// </summary>
public interface ILeaveAttachmentService
{
    /// <summary>
    /// Validates, scans, stores, and persists one supporting document for the calling employee.
    /// Fails 400 for a missing/oversized file (<c>file_too_large</c>), a disallowed or spoofed type, or an
    /// unclean scan; 403 when the caller has no employee record.
    /// </summary>
    Task<Result<LeaveAttachmentDto>> UploadAsync(
        UploadLeaveAttachmentInput input, CancellationToken cancellationToken = default);
}
