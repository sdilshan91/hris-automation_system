namespace HRM.Domain.Enums;

/// <summary>
/// Where an employee is expected to work (US-CHR-013 / US-ATT-011 AC-5).
///
/// <para>The ONLY behavioural difference today is the attendance geo-fence: a <see cref="Remote"/>
/// employee is exempt from the geo-fence radius check on clock-in, because they have no branch to be
/// near. <see cref="OnSite"/> and <see cref="Hybrid"/> remain fully enforced (US-ATT-001 AC-6 / BR-8) —
/// a hybrid employee is still expected at the office on their office days, and the module has no
/// which-days-are-office-days concept to distinguish them.</para>
///
/// <para>The exemption is geo-fence ONLY. RequireGeolocation, the IP allowlist and the photo requirement
/// are separate business rules and apply to every arrangement.</para>
/// </summary>
public enum WorkArrangement
{
    /// <summary>Works from a company location. The default — geo-fence fully enforced.</summary>
    OnSite = 0,

    /// <summary>Splits time between a company location and elsewhere. Geo-fence fully enforced.</summary>
    Hybrid = 1,

    /// <summary>Works away from any company location. Exempt from the geo-fence radius check.</summary>
    Remote = 2,
}
