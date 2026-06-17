using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationPreferences.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Notifications;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-003: per-user notification preferences. Implements both the matrix CRUD (request scope, scoped to
/// <c>ICurrentUser</c>) AND the dispatch-time <see cref="INotificationPreferenceService.ShouldDeliverAsync"/>
/// check (takes tenant + user explicitly so it works from background jobs / the notification outbox).
///
/// <para><b>Cascade (FR-5/BR-1):</b> the effective matrix is the user's saved override rows merged OVER the
/// static <see cref="NotificationPreferenceDefaults"/> catalog — a category with no saved row uses its
/// default. <b>Mandatory (BR-2/AC-3/AC-4):</b> SecurityAlerts (the only mandatory category today) always
/// reports both channels on, cannot be disabled, and always delivers. <b>BR-3:</b> a non-mandatory category
/// may not have BOTH channels off. <b>Quiet hours (FR-9/BR-5):</b> email during the user's quiet-hours window
/// is DEFERRED (returns a send-after-quiet-hours-end signal) rather than dropped; in-app is always immediate.</para>
///
/// <para><b>Cache (NFR-3):</b> the dispatch lookup is cached in Redis (TTL 5 min) under
/// <c>t:{tenantId}:u:{userId}:notif-prefs</c> WHEN an <see cref="IDistributedCache"/> is registered; with no
/// Redis the lookup goes straight to the DB. Redis is never a hard dependency — the cache is optional and a
/// cache outage never fails a check. The cache is invalidated on every preference change.</para>
/// </summary>
public sealed class NotificationPreferenceService : INotificationPreferenceService
{
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
    };

    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;
    private readonly IDistributedCache? _cache;
    private readonly ILogger<NotificationPreferenceService> _logger;

    public NotificationPreferenceService(
        AppDbContext db,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<NotificationPreferenceService> logger,
        TimeProvider? timeProvider = null,
        IDistributedCache? cache = null)
    {
        _db = db;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _cache = cache;
    }

    // ── GET matrix (AC-1) ───────────────────────────────────────────────────

    public async Task<Result<PreferenceMatrixDto>> GetMatrixAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PreferenceMatrixDto>.Failure("Tenant not resolved.", 400);
        if (!_currentUser.IsAuthenticated)
            return Result<PreferenceMatrixDto>.Failure("Not authenticated.", 401);

        var saved = await LoadSavedAsync(_currentUser.UserId, cancellationToken);
        return Result<PreferenceMatrixDto>.Success(BuildMatrix(saved));
    }

    // ── Dispatch-time check (FR-6/BR-5) ─────────────────────────────────────

    public async Task<DeliveryDecision> ShouldDeliverAsync(
        Guid tenantId,
        Guid userId,
        NotificationCategory category,
        NotificationChannel channel,
        CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(tenantId, userId, cancellationToken);
        var pref = snapshot.For(category);

        // BR-2: mandatory categories always deliver regardless of toggles (and ignore quiet hours — they are
        // security/critical). In-app and email both go out.
        if (pref.IsMandatory)
            return DeliveryDecision.Deliver();

        var channelOn = channel == NotificationChannel.InApp ? pref.ChannelInApp : pref.ChannelEmail;
        if (!channelOn)
            return DeliveryDecision.Suppressed();

        // BR-5: in-app is always immediate; quiet hours only affect EMAIL timing.
        if (channel == NotificationChannel.InApp)
            return DeliveryDecision.Deliver();

        // Email + enabled: apply quiet hours if configured and currently active in the user's timezone.
        if (snapshot.QuietHours.Enabled
            && TryResolveQuietHoursDefer(snapshot.QuietHours, out var deferUntilUtc))
        {
            return DeliveryDecision.Defer(deferUntilUtc);
        }

        return DeliveryDecision.Deliver();
    }

    // ── Update one category (AC-2/AC-3/AC-4, BR-2/BR-3) ─────────────────────

    public async Task<Result<CategoryPreferenceDto>> UpdateCategoryAsync(
        NotificationCategory category,
        bool channelInApp,
        bool channelEmail,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CategoryPreferenceDto>.Failure("Tenant not resolved.", 400);
        if (!_currentUser.IsAuthenticated)
            return Result<CategoryPreferenceDto>.Failure("Not authenticated.", 401);

        var def = NotificationPreferenceDefaults.For(category);

        // BR-2/AC-3/AC-4: a mandatory category cannot be disabled — both channels must stay on.
        if (def.IsMandatory && (!channelInApp || !channelEmail))
        {
            return Result<CategoryPreferenceDto>.Failure(
                $"{def.DisplayName} cannot be disabled. This is required by your organization's policy.",
                422, "category_mandatory");
        }

        // BR-3: a non-mandatory category must keep at least one channel enabled.
        if (!def.IsMandatory && !channelInApp && !channelEmail)
        {
            return Result<CategoryPreferenceDto>.Failure(
                $"At least one channel must remain enabled for {def.DisplayName}.",
                422, "both_channels_off");
        }

        var userId = _currentUser.UserId;
        var existing = await _db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Category == category, cancellationToken);

        if (existing is null)
        {
            // Carry the user's current quiet-hours window onto the new row so it stays consistent across rows.
            var (qhStart, qhEnd, qhTz) = await LoadQuietHoursAsync(userId, cancellationToken);
            existing = new NotificationPreference
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                UserId = userId,
                Category = category,
                IsMandatory = def.IsMandatory,
                QuietHoursStart = qhStart,
                QuietHoursEnd = qhEnd,
                QuietHoursTimezone = qhTz,
            };
            _db.NotificationPreferences.Add(existing);
        }

        existing.ChannelInApp = def.IsMandatory || channelInApp;
        existing.ChannelEmail = def.IsMandatory || channelEmail;
        existing.IsMandatory = def.IsMandatory;
        existing.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

        await _db.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(_tenantContext.TenantId, userId, cancellationToken);

        return Result<CategoryPreferenceDto>.Success(ToDto(def, existing.ChannelInApp, existing.ChannelEmail));
    }

    // ── Update quiet hours (FR-9/BR-5) ──────────────────────────────────────

    public async Task<Result<QuietHoursDto>> UpdateQuietHoursAsync(
        bool enabled,
        TimeOnly? start,
        TimeOnly? end,
        string? timezone,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<QuietHoursDto>.Failure("Tenant not resolved.", 400);
        if (!_currentUser.IsAuthenticated)
            return Result<QuietHoursDto>.Failure("Not authenticated.", 401);

        TimeOnly? effStart = null, effEnd = null;
        string? effTz = null;

        if (enabled)
        {
            if (start is null || end is null || string.IsNullOrWhiteSpace(timezone))
                return Result<QuietHoursDto>.Failure(
                    "Start, end and timezone are required when quiet hours are enabled.", 422);

            if (!IsValidTimezone(timezone))
                return Result<QuietHoursDto>.Failure(
                    $"'{timezone}' is not a recognized IANA timezone.", 422, "invalid_timezone");

            effStart = start;
            effEnd = end;
            effTz = timezone.Trim();
        }

        var userId = _currentUser.UserId;
        var rows = await _db.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (rows.Count == 0)
        {
            // No saved overrides yet: create a single carrier row for the (non-mandatory) first category so the
            // quiet-hours window is persisted. It keeps that category's default channel state, so the matrix is
            // unchanged apart from quiet hours.
            var carrierCategory = NotificationPreferenceDefaults.All.First(d => !d.IsMandatory);
            _db.NotificationPreferences.Add(new NotificationPreference
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                UserId = userId,
                Category = carrierCategory.Category,
                ChannelInApp = carrierCategory.DefaultInApp,
                ChannelEmail = carrierCategory.DefaultEmail,
                IsMandatory = carrierCategory.IsMandatory,
                QuietHoursStart = effStart,
                QuietHoursEnd = effEnd,
                QuietHoursTimezone = effTz,
            });
        }
        else
        {
            foreach (var row in rows)
            {
                row.QuietHoursStart = effStart;
                row.QuietHoursEnd = effEnd;
                row.QuietHoursTimezone = effTz;
                row.UpdatedAt = now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        await InvalidateCacheAsync(_tenantContext.TenantId, userId, cancellationToken);

        return Result<QuietHoursDto>.Success(new QuietHoursDto(enabled, effStart, effEnd, effTz));
    }

    // ── Reset (FR-7) ────────────────────────────────────────────────────────

    public async Task<Result<PreferenceMatrixDto>> ResetAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PreferenceMatrixDto>.Failure("Tenant not resolved.", 400);
        if (!_currentUser.IsAuthenticated)
            return Result<PreferenceMatrixDto>.Failure("Not authenticated.", 401);

        var userId = _currentUser.UserId;
        var rows = await _db.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        if (rows.Count > 0)
        {
            // Hard-delete the override rows so the matrix falls back to tenant defaults (FR-7).
            _db.NotificationPreferences.RemoveRange(rows);
            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateCacheAsync(_tenantContext.TenantId, userId, cancellationToken);
        }

        // No saved rows → pure defaults.
        return Result<PreferenceMatrixDto>.Success(BuildMatrix([]));
    }

    // ── Internals ───────────────────────────────────────────────────────────

    private async Task<List<NotificationPreference>> LoadSavedAsync(Guid userId, CancellationToken cancellationToken)
        => await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

    private async Task<(TimeOnly? Start, TimeOnly? End, string? Tz)> LoadQuietHoursAsync(
        Guid userId, CancellationToken cancellationToken)
    {
        var row = await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.QuietHoursStart != null)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? (null, null, null) : (row.QuietHoursStart, row.QuietHoursEnd, row.QuietHoursTimezone);
    }

    /// <summary>
    /// Builds the effective matrix: every category from the defaults catalog, with the user's saved override
    /// merged on top when present. Quiet hours are read from any saved row (they are a per-user setting).
    /// </summary>
    private static PreferenceMatrixDto BuildMatrix(IReadOnlyList<NotificationPreference> saved)
    {
        var byCategory = saved.ToDictionary(p => p.Category);

        var categories = NotificationPreferenceDefaults.All
            .Select(def =>
            {
                if (def.IsMandatory)
                    return ToDto(def, true, true); // mandatory: forced on regardless of any stored value

                if (byCategory.TryGetValue(def.Category, out var row))
                    return ToDto(def, row.ChannelInApp, row.ChannelEmail);

                return ToDto(def, def.DefaultInApp, def.DefaultEmail);
            })
            .ToList();

        var quietRow = saved.FirstOrDefault(p => p.QuietHoursStart != null);
        var quietHours = quietRow is null
            ? new QuietHoursDto(false, null, null, null)
            : new QuietHoursDto(true, quietRow.QuietHoursStart, quietRow.QuietHoursEnd, quietRow.QuietHoursTimezone);

        return new PreferenceMatrixDto(categories, quietHours);
    }

    private static CategoryPreferenceDto ToDto(NotificationCategoryDefault def, bool inApp, bool email) =>
        new(def.Category.ToString(), def.DisplayName, def.Description, inApp, email, def.IsMandatory);

    /// <summary>
    /// Reads the dispatch snapshot for (tenant, user) — from Redis when configured (NFR-3), else the DB.
    /// Always falls back to a live DB read on a cache miss or any cache error (Redis is optional).
    /// </summary>
    private async Task<PreferenceSnapshot> GetSnapshotAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var cacheKey = CacheKey(tenantId, userId);

        if (_cache is not null)
        {
            try
            {
                var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
                if (cached is not null)
                    return JsonSerializer.Deserialize<PreferenceSnapshot>(cached) ?? PreferenceSnapshot.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification-prefs cache read failed for {Key}; falling back to DB.", cacheKey);
            }
        }

        var snapshot = await BuildSnapshotFromDbAsync(userId, cancellationToken);

        if (_cache is not null)
        {
            try
            {
                await _cache.SetStringAsync(
                    cacheKey, JsonSerializer.Serialize(snapshot), CacheOptions, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Notification-prefs cache write failed for {Key}.", cacheKey);
            }
        }

        return snapshot;
    }

    private async Task<PreferenceSnapshot> BuildSnapshotFromDbAsync(Guid userId, CancellationToken cancellationToken)
    {
        // The dispatch path may run from a background job with no resolved tenant; the global query filter
        // is a no-op then, so we filter by userId only — the caller passes the trusted tenant/user, and rows
        // are tenant-stamped, so a per-user read within a job is already scoped to that user's rows.
        var rows = await _db.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);

        var byCategory = rows.ToDictionary(p => p.Category);
        var entries = NotificationPreferenceDefaults.All.Select(def =>
        {
            if (def.IsMandatory)
                return new SnapshotEntry(def.Category, true, true, true);

            if (byCategory.TryGetValue(def.Category, out var row))
                return new SnapshotEntry(def.Category, row.ChannelInApp, row.ChannelEmail, false);

            return new SnapshotEntry(def.Category, def.DefaultInApp, def.DefaultEmail, false);
        }).ToList();

        var quietRow = rows.FirstOrDefault(p => p.QuietHoursStart != null);
        var quiet = quietRow is null
            ? new SnapshotQuietHours(false, null, null, null)
            : new SnapshotQuietHours(
                true,
                quietRow.QuietHoursStart?.ToString("HH:mm:ss"),
                quietRow.QuietHoursEnd?.ToString("HH:mm:ss"),
                quietRow.QuietHoursTimezone);

        return new PreferenceSnapshot(entries, quiet);
    }

    /// <summary>
    /// Computes the next UTC instant the quiet-hours window ends, if NOW (in the user's timezone) is inside the
    /// window. Returns false (no defer) when outside the window or the timezone cannot be resolved.
    /// Handles overnight windows (e.g. 22:00–07:00) where end &lt; start.
    /// </summary>
    private bool TryResolveQuietHoursDefer(SnapshotQuietHours qh, out DateTime deferUntilUtc)
    {
        deferUntilUtc = default;

        if (!qh.Enabled || qh.Start is null || qh.End is null || string.IsNullOrWhiteSpace(qh.Timezone))
            return false;

        if (!TryGetTimeZone(qh.Timezone, out var tz)
            || !TimeOnly.TryParse(qh.Start, out var start)
            || !TimeOnly.TryParse(qh.End, out var end))
            return false;

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var nowTime = TimeOnly.FromDateTime(localNow);

        var overnight = end <= start;
        var inside = overnight
            ? nowTime >= start || nowTime < end   // wraps midnight
            : nowTime >= start && nowTime < end;

        if (!inside)
            return false;

        // The next local date/time the window ends.
        var endLocal = localNow.Date + end.ToTimeSpan();
        if (overnight && nowTime >= start)
            endLocal = endLocal.AddDays(1); // we're before midnight; end is tomorrow morning
        else if (endLocal <= localNow)
            endLocal = endLocal.AddDays(1);

        deferUntilUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(endLocal, DateTimeKind.Unspecified), tz);
        return true;
    }

    private static bool IsValidTimezone(string? timezone) => TryGetTimeZone(timezone, out _);

    private static bool TryGetTimeZone(string? timezone, out TimeZoneInfo tz)
    {
        tz = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(timezone))
            return false;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(timezone.Trim());
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task InvalidateCacheAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        if (_cache is null)
            return;
        try
        {
            await _cache.RemoveAsync(CacheKey(tenantId, userId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evict notification-prefs cache for user {UserId}.", userId);
        }
    }

    private static string CacheKey(Guid tenantId, Guid userId) => $"t:{tenantId}:u:{userId}:notif-prefs";

    // ── Cache snapshot shape (JSON-serializable) ────────────────────────────

    private sealed record PreferenceSnapshot(
        IReadOnlyList<SnapshotEntry> Entries,
        SnapshotQuietHours QuietHours)
    {
        public static PreferenceSnapshot Empty { get; } =
            new([], new SnapshotQuietHours(false, null, null, null));

        public SnapshotEntry For(NotificationCategory category)
        {
            foreach (var e in Entries)
                if (e.Category == category)
                    return e;
            var def = NotificationPreferenceDefaults.For(category);
            return new SnapshotEntry(category, def.DefaultInApp, def.DefaultEmail, def.IsMandatory);
        }
    }

    private sealed record SnapshotEntry(NotificationCategory Category, bool ChannelInApp, bool ChannelEmail, bool IsMandatory);

    private sealed record SnapshotQuietHours(bool Enabled, string? Start, string? End, string? Timezone);
}
