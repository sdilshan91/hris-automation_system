namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>A component link within a structure, projected with the linked component's display fields (FR-3).</summary>
public sealed record SalaryStructureComponentDto
{
    public Guid Id { get; init; }
    public Guid SalaryComponentId { get; init; }
    public string ComponentName { get; init; } = string.Empty;
    public string ComponentCode { get; init; } = string.Empty;
    public string ComponentType { get; init; } = string.Empty;
    public string CalculationMethod { get; init; } = string.Empty;
    public decimal? OverrideValue { get; init; }
    public string? OverrideFormula { get; init; }
    public decimal? EffectiveValue { get; init; }
    public string? EffectiveFormula { get; init; }
    public int ProcessingOrder { get; init; }
    public bool IsMandatory { get; init; }
}

/// <summary>Full salary-structure representation including its component links (US-PAY-001 §7).</summary>
public sealed record SalaryStructureDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<SalaryStructureComponentDto> Components { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>Lightweight structure row for the paged list view (NFR-3).</summary>
public sealed record SalaryStructureListItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public DateOnly EffectiveFrom { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public int ComponentCount { get; init; }
    public DateTime CreatedAt { get; init; }
}

// ── Request bodies ───────────────────────────────────────────────────

/// <summary>One component link in a create/update structure request (FR-3).</summary>
public sealed record SalaryStructureComponentInputDto
{
    public Guid SalaryComponentId { get; init; }
    public decimal? OverrideValue { get; init; }
    public string? OverrideFormula { get; init; }
    public int ProcessingOrder { get; init; }
    public bool IsMandatory { get; init; }
}

/// <summary>Request body for creating a salary structure (FR-2/FR-3).</summary>
public sealed record CreateSalaryStructureRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<SalaryStructureComponentInputDto> Components { get; init; } = [];
}

/// <summary>Request body for updating a salary structure (FR-2/FR-3).</summary>
public sealed record UpdateSalaryStructureRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateOnly EffectiveFrom { get; init; }
    public bool IsDefault { get; init; }
    public bool IsActive { get; init; }
    public IReadOnlyList<SalaryStructureComponentInputDto> Components { get; init; } = [];
}

/// <summary>Request body for linking a single component to a structure (FR-3).</summary>
public sealed record LinkComponentRequest
{
    public Guid SalaryComponentId { get; init; }
    public decimal? OverrideValue { get; init; }
    public string? OverrideFormula { get; init; }
    public int ProcessingOrder { get; init; }
    public bool IsMandatory { get; init; }
}

/// <summary>One ordered entry for the reorder request (AC-4).</summary>
public sealed record ReorderComponentEntry
{
    public Guid SalaryStructureComponentId { get; init; }
    public int ProcessingOrder { get; init; }
}

/// <summary>Request body for reordering component processing priority within a structure (AC-4).</summary>
public sealed record ReorderComponentsRequest
{
    public IReadOnlyList<ReorderComponentEntry> Order { get; init; } = [];
}

/// <summary>Request body for cloning a salary structure (FR-6).</summary>
public sealed record CloneSalaryStructureRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
}

/// <summary>Frontend list-response contract: { data, total, page, pageSize }.</summary>
public sealed record SalaryStructurePageResponse
{
    public IReadOnlyList<SalaryStructureListItemDto> Data { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
