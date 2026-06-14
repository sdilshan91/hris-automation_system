using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// Represents a holiday in a tenant's holiday calendar (US-LV-007 §7).
/// Supports tenant-scoped CRUD, optional location filtering, soft-delete,
/// and recurring annual generation. Public holidays are excluded from leave
/// day calculations (AC-2) via the DB-backed <c>IHolidayProvider</c>.
/// </summary>
public sealed class Holiday : BaseEntity
{
    /// <summary>
    /// Holiday name, e.g. "New Year's Day" (FR-2).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Calendar date of the holiday (FR-2). Date-only, no time component.
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Holiday classification: public, restricted, or optional (FR-2).
    /// Stored as a string. Only public holidays are excluded from leave day counts (AC-2).
    /// </summary>
    public HolidayType Type { get; set; } = HolidayType.Public;

    /// <summary>
    /// Optional location scope (FR-2). Null means the holiday applies to all
    /// locations in the tenant. When set, only employees at that location are
    /// affected (BR-2, test hint: NY holiday doesn't affect London).
    /// </summary>
    public Guid? LocationId { get; set; }

    /// <summary>
    /// Optional free-text description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this holiday recurs annually (FR-3, BR-5). When true, the
    /// HolidayRecurrenceJob auto-generates next-year entries 30 days before year-end.
    /// </summary>
    public bool IsRecurring { get; set; }

    /// <summary>
    /// Whether this holiday is active. Deactivated holidays are excluded from
    /// leave calculations and calendar views but retained for history (BR-4).
    /// </summary>
    public bool IsActive { get; set; } = true;

    // -- Navigation --

    /// <summary>
    /// Optional location this holiday is scoped to (null = all locations).
    /// </summary>
    public Location? Location { get; set; }
}
