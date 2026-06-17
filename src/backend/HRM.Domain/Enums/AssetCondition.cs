namespace HRM.Domain.Enums;

/// <summary>
/// Physical condition of a company asset at issuance (US-ONB-004 §7, FR-2). Stored as a string column for
/// readability/forward-compat (see AssetConfiguration). Mirrors the colored-chip selector in the UI.
/// </summary>
public enum AssetCondition
{
    /// <summary>Brand new (green chip).</summary>
    New = 0,

    /// <summary>Used but in good working order (blue chip).</summary>
    Good = 1,

    /// <summary>Showing wear but functional (yellow chip).</summary>
    Fair = 2,

    /// <summary>Worn / nearing end-of-life (red chip).</summary>
    Poor = 3,
}
