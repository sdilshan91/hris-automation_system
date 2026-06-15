namespace HRM.Domain.Enums;

/// <summary>
/// The medium an interview is conducted in (US-REC-005 FR-1). Drives the conditional location /
/// video-link requirement: <see cref="InPerson"/> requires a location, <see cref="Video"/> requires a
/// video meeting link, <see cref="Phone"/> requires neither. Stored as a string column for
/// readability/forward-compat (see InterviewConfiguration).
/// </summary>
public enum InterviewType
{
    /// <summary>An on-site / face-to-face interview — requires a location (FR-1).</summary>
    InPerson = 0,

    /// <summary>A video-call interview — requires a video meeting link (FR-1).</summary>
    Video = 1,

    /// <summary>A phone interview — requires neither a location nor a video link (FR-1).</summary>
    Phone = 2,
}
