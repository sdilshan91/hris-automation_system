using HRM.Domain.Enums;

namespace HRM.Domain.Entities;

/// <summary>
/// A single user's saved override for one notification category (US-NTF-003 §7, FR-1/FR-8). Tenant-scoped
/// via <see cref="BaseEntity"/> (TenantId auto-stamped by TenantInterceptor and read-isolated by the
/// AppDbContext global query filter) — so preferences are per-tenant-membership and independent across
/// tenants (BR-4/AC-5). Unique per (tenant, user, category): a row exists only when the user has
/// customized that category; otherwise the
/// <see cref="HRM.Domain.Notifications.NotificationPreferenceDefaults"/> catalog supplies the effective
/// value (FR-5 cascade, BR-1).
/// </summary>
public sealed class NotificationPreference : BaseEntity
{
    /// <summary>
    /// The platform user this preference belongs to (FR-8). Maps to the authenticated user (UserTenant.UserId),
    /// matching <see cref="Notification.UserId"/>.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>The notification category this row tunes (FR-2). Stored as a string column.</summary>
    public NotificationCategory Category { get; set; }

    /// <summary>Whether in-app delivery is enabled for this category (FR-3). Default true.</summary>
    public bool ChannelInApp { get; set; } = true;

    /// <summary>Whether email delivery is enabled for this category (FR-3). Default true.</summary>
    public bool ChannelEmail { get; set; } = true;

    /// <summary>
    /// Whether this category is mandatory and cannot be opted out of (FR-4/BR-2). Today this mirrors the
    /// static <see cref="HRM.Domain.Notifications.NotificationPreferenceDefaults"/> flag; the column exists so
    /// per-tenant mandatory configuration (Tenant-Admin console) can populate it without a schema change.
    /// </summary>
    public bool IsMandatory { get; set; }

    /// <summary>
    /// Quiet-hours window start in the user's timezone (FR-9/BR-5). Null when quiet hours are not configured.
    /// Stored as postgres <c>time</c>. Quiet hours are a per-user setting, so the same value is repeated on
    /// each of the user's preference rows (the service reads it from any row).
    /// </summary>
    public TimeOnly? QuietHoursStart { get; set; }

    /// <summary>Quiet-hours window end in the user's timezone (FR-9/BR-5). Null when not configured.</summary>
    public TimeOnly? QuietHoursEnd { get; set; }

    /// <summary>IANA timezone the quiet-hours window is interpreted in, e.g. "Asia/Colombo" (FR-9). Null when not set.</summary>
    public string? QuietHoursTimezone { get; set; }
}
