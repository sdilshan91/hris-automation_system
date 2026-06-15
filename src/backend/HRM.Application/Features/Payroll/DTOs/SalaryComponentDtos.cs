using HRM.Domain.Enums;

namespace HRM.Application.Features.Payroll.DTOs;

/// <summary>Full salary-component representation for detail + create/update responses (US-PAY-001 §7).</summary>
public sealed record SalaryComponentDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public SalaryComponentType Type { get; init; }
    public string TypeName { get; init; } = string.Empty;
    public CalculationMethod CalculationMethod { get; init; }
    public string CalculationMethodName { get; init; } = string.Empty;
    public decimal? DefaultValue { get; init; }
    public string? FormulaExpression { get; init; }
    public bool IsTaxable { get; init; }
    public bool IsStatutory { get; init; }
    public bool IsActive { get; init; }
    public int ProcessingOrder { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>Lightweight component row for the paged list view (NFR-3).</summary>
public sealed record SalaryComponentListItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public SalaryComponentType Type { get; init; }
    public string TypeName { get; init; } = string.Empty;
    public CalculationMethod CalculationMethod { get; init; }
    public string CalculationMethodName { get; init; } = string.Empty;
    public decimal? DefaultValue { get; init; }
    public bool IsTaxable { get; init; }
    public bool IsStatutory { get; init; }
    public bool IsActive { get; init; }
    public int ProcessingOrder { get; init; }
}

// ── Request bodies ───────────────────────────────────────────────────

/// <summary>Request body for creating a salary component (FR-1).</summary>
public sealed record CreateSalaryComponentRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public SalaryComponentType Type { get; init; }
    public CalculationMethod CalculationMethod { get; init; }
    public decimal? DefaultValue { get; init; }
    public string? FormulaExpression { get; init; }
    public bool IsTaxable { get; init; } = true;
    public bool IsStatutory { get; init; }
    public bool IsActive { get; init; } = true;
    public int ProcessingOrder { get; init; }
}

/// <summary>Request body for updating a salary component (FR-1/AC-2).</summary>
public sealed record UpdateSalaryComponentRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public SalaryComponentType Type { get; init; }
    public CalculationMethod CalculationMethod { get; init; }
    public decimal? DefaultValue { get; init; }
    public string? FormulaExpression { get; init; }
    public bool IsTaxable { get; init; } = true;
    public bool IsStatutory { get; init; }
    public bool IsActive { get; init; } = true;
    public int ProcessingOrder { get; init; }
}

/// <summary>Frontend list-response contract: { data, total, page, pageSize }.</summary>
public sealed record SalaryComponentPageResponse
{
    public IReadOnlyList<SalaryComponentListItemDto> Data { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

/// <summary>Generic paged result envelope for payroll list endpoints (mirrors the recruitment pattern).</summary>
public sealed record PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
