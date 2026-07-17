namespace HRM.Domain.Enums;

/// <summary>
/// Channel through which an application was submitted (US-REC-002 §7 "source"). Stored as a string
/// column for readability (see ApplicantConfiguration).
/// </summary>
public enum ApplicationSource
{
    /// <summary>Anonymous submission via the public careers page (AC-1).</summary>
    Public = 0,

    /// <summary>Submission by an authenticated existing employee (AC-4/FR-8/BR-5).</summary>
    Internal = 1,

    /// <summary>Submission attributed to an employee referral (forward-compat; not produced by US-REC-002 directly).</summary>
    Referral = 2,

    /// <summary>
    /// Sentinel for a persisted <c>source</c> string outside this enum (ISSUE-231). Never written by the app —
    /// the write path rejects it (SubmitApplicationValidator) — and only produced on READ by
    /// <see cref="Persistence.Converters.TolerantEnumToStringConverter{TEnum}"/> so one corrupt row does not
    /// 500 the whole pipeline board. Appended (not 0) so it does not disturb the existing values.
    /// </summary>
    Unknown = 99,
}
