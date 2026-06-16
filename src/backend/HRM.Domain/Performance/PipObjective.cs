using HRM.Domain.Entities;

namespace HRM.Domain.Performance;

/// <summary>
/// One improvement objective of a <see cref="Pip"/> (US-PRF-008 AC-1/FR-1): a measurable goal with success
/// criteria and a due date. Added at PIP creation and, optionally, when the PIP is extended (FR-6). Tenant-scoped
/// via <see cref="BaseEntity.TenantId"/> + the EF global query filter + <c>TenantInterceptor</c> (NFR-2). Maps to
/// the "pip_objective" table.
/// </summary>
public sealed class PipObjective : BaseEntity
{
    /// <summary>The parent PIP (FK, required).</summary>
    public Guid PipId { get; set; }

    /// <summary>Objective title (max 200 chars, FR-1).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Objective description (max 2000 chars, FR-1).</summary>
    public string? Description { get; set; }

    /// <summary>Measurable success criteria (max 2000 chars, FR-1).</summary>
    public string SuccessCriteria { get; set; } = string.Empty;

    /// <summary>The objective due date (FR-1).</summary>
    public DateOnly DueDate { get; set; }

    /// <summary>Display order within the PIP.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// True when this objective was added at an extension (FR-6) rather than at initial creation. Lets the FE
    /// distinguish original vs. added objectives.
    /// </summary>
    public bool AddedAtExtension { get; set; }

    // ── Navigation ─────────────────────────────────────────────────────
    public Pip? Pip { get; set; }
}
