namespace HRM.Domain.Entities;

/// <summary>
/// Represents an organizational department within a tenant.
/// Supports hierarchical parent-child relationships via self-referencing FK.
/// US-CHR-004: Create and Manage Departments.
/// </summary>
public sealed class Department : BaseEntity
{
    /// <summary>
    /// Department name, unique within a tenant (FR-2, BR-1).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Short code for the department, unique within a tenant.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Optional free-text description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Self-referencing FK for hierarchy (FR-3, BR-3, BR-4).
    /// Null means root department.
    /// </summary>
    public Guid? ParentDepartmentId { get; set; }

    /// <summary>
    /// Optional department manager. FK to Employee (US-CHR-001).
    /// ON DELETE SET NULL — removing the employee clears the manager slot.
    /// </summary>
    public Guid? ManagerId { get; set; }

    /// <summary>
    /// Whether the department is active (FR-7, BR-5).
    /// Deactivated departments are hidden from dropdowns but visible in admin views.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // ── Navigation ─────────────────────────────────────────────────

    /// <summary>
    /// Parent department (null for root departments).
    /// </summary>
    public Department? ParentDepartment { get; set; }

    /// <summary>
    /// Child departments forming the subtree.
    /// </summary>
    public ICollection<Department> ChildDepartments { get; set; } = new List<Department>();

    /// <summary>
    /// Department manager (Employee). Wired by US-CHR-001.
    /// </summary>
    public Employee? Manager { get; set; }

    /// <summary>
    /// Employees assigned to this department.
    /// </summary>
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
