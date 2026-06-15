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
}
