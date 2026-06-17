using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationPreferences.DTOs;
using HRM.Domain.Enums;

namespace HRM.Application.Common.Interfaces;

/// <summary>The two delivery channels a preference can toggle (US-NTF-003 FR-3). SMS is Phase 2.</summary>
public enum NotificationChannel
{
    InApp = 0,
    Email = 1,
}

/// <summary>
/// The dispatch-time decision the Notification Dispatcher gets back from
/// <see cref="INotificationPreferenceService.ShouldDeliverAsync"/> (US-NTF-003 FR-6/BR-5).
///
/// <list type="bullet">
/// <item><b>Deliver</b> — send now (the channel is enabled, or the category is mandatory).</item>
/// <item><b>Suppressed</b> — the user disabled this channel for this category; drop it.</item>
/// <item><b>DeferredUntilQuietHoursEnd</b> — email only: quiet hours are active in the user's timezone, so
/// queue the email and send it after <see cref="DeferUntilUtc"/> (BR-5). In-app is never deferred.</item>
/// </list>
/// </summary>
public enum DeliveryDecisionKind
{
    Deliver = 0,
    Suppressed = 1,
    DeferredUntilQuietHoursEnd = 2,
}

/// <summary>
/// The result of a dispatch-time preference check (US-NTF-003 FR-6/BR-5). Simple, documented shape: the
/// dispatcher branches on <see cref="Kind"/> and, when deferred, schedules the email for
/// <see cref="DeferUntilUtc"/>.
/// </summary>
public sealed record DeliveryDecision(DeliveryDecisionKind Kind, DateTime? DeferUntilUtc = null)
{
    public bool ShouldDeliverNow => Kind == DeliveryDecisionKind.Deliver;

    public static DeliveryDecision Deliver() => new(DeliveryDecisionKind.Deliver);
    public static DeliveryDecision Suppressed() => new(DeliveryDecisionKind.Suppressed);
    public static DeliveryDecision Defer(DateTime untilUtc) =>
        new(DeliveryDecisionKind.DeferredUntilQuietHoursEnd, untilUtc);
}

/// <summary>
/// US-NTF-003 dispatch-time seam: the single service the Notification Dispatcher (and the matrix UI) calls
/// to read a user's effective notification preferences. Saved per-category overrides are merged over the
/// static <see cref="HRM.Domain.Notifications.NotificationPreferenceDefaults"/> catalog (FR-5 cascade, BR-1).
///
/// <para>Tenant scope: matrix reads run in the resolved request scope and are scoped to the calling user via
/// <c>ICurrentUser</c>; the dispatch check takes the tenant + user EXPLICITLY so it works from background
/// jobs / the notification outbox (no resolved ITenantContext), mirroring <see cref="INotificationService"/>.</para>
/// </summary>
public interface INotificationPreferenceService
{
    /// <summary>
    /// The effective preference matrix for the CURRENT user (merged overrides over defaults) + quiet hours
    /// (AC-1, GET). Scoped to <c>ICurrentUser.UserId</c> within the resolved tenant.
    /// </summary>
    Task<Result<PreferenceMatrixDto>> GetMatrixAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The dispatch-time check (FR-6). Returns whether <paramref name="channel"/> should deliver a
    /// <paramref name="category"/> notification to <paramref name="userId"/> in <paramref name="tenantId"/>.
    /// Mandatory categories always deliver (BR-2). For email + active quiet hours the result is a DEFER signal
    /// (BR-5) rather than a drop; in-app is always immediate.
    /// </summary>
    Task<DeliveryDecision> ShouldDeliverAsync(
        Guid tenantId,
        Guid userId,
        NotificationCategory category,
        NotificationChannel channel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update one category's channel toggles for the CURRENT user (PUT /{category}). Enforces BR-2 (mandatory
    /// cannot be disabled) and BR-3 (non-mandatory must keep ≥1 channel). Returns the merged row.
    /// </summary>
    Task<Result<CategoryPreferenceDto>> UpdateCategoryAsync(
        NotificationCategory category,
        bool channelInApp,
        bool channelEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the CURRENT user's quiet-hours window (PUT /quiet-hours). When disabled the window is cleared;
    /// when enabled the IANA timezone is validated and start/end persisted across the user's preference rows.
    /// </summary>
    Task<Result<QuietHoursDto>> UpdateQuietHoursAsync(
        bool enabled,
        TimeOnly? start,
        TimeOnly? end,
        string? timezone,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reset: delete the CURRENT user's override rows so the matrix reverts to tenant defaults (POST /reset,
    /// FR-7). Returns the reverted-to-defaults matrix.
    /// </summary>
    Task<Result<PreferenceMatrixDto>> ResetAsync(CancellationToken cancellationToken = default);
}
