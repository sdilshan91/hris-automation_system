using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace HRM.Infrastructure.Services;

/// <summary>
/// CAL-4b / US-ATT-011 AC-3: the ADMIN CRUD surface for the attendance policy — the tenant default and
/// its per-Location overrides. Until CAL-4b <c>AttendanceSettings</c> had NO write path at all: all policy
/// fields were lazily created at C# defaults and were never editable.
///
/// <para>⚠ <b>THE INVARIANT.</b> <c>AttendanceSettings</c> is NOT "one row per tenant" — it is one row per
/// (tenant, location): LocationId null = the tenant default, LocationId set = that Location's override.
/// <b>Every read and write below is explicitly predicated on LocationId.</b> An unpredicated
/// <c>FirstOrDefaultAsync()</c> returns an ARBITRARY row and would apply one branch's geofence/overtime
/// policy to the whole tenant. CAL-4a had to repoint seven such call sites — do not create an eighth.</para>
///
/// <para><b>Row-level semantics.</b> An override row IS that Location's complete policy; it does not merge
/// field-by-field with the tenant row, and creating one does not copy the tenant row. Deleting an override
/// is what makes that Location's employees fall back to the tenant default (via AttendancePolicyResolver).</para>
///
/// <para><b>Upsert = FULL REPLACE</b> of that scope's policy (BUG-117 class — an omitted JSON field takes
/// the DTO default and therefore RESETS that setting; the client contract is GET-then-PUT). Documented at
/// the seam on <see cref="AttendanceSettingsDto"/>; deliberately not fixed here.</para>
///
/// <para><b>Audit.</b> <c>AttendanceSettings</c> is NOT marked <c>IAuditExempt</c>, so since BUG-082 the
/// opt-OUT <c>AuditCaptureInterceptor</c> already captures every INSERT/UPDATE/DELETE here into audit_log
/// with property-level before/after diffs. No hand-rolled audit rows — that would double-audit.</para>
///
/// <para><b>Isolation.</b> The EF global query filter + TenantInterceptor. Location lookups run under the
/// filter, so a cross-tenant locationId is simply NOT FOUND and is rejected as "invalid_location" rather
/// than leaking — never IgnoreQueryFilters here.</para>
/// </summary>
public sealed class AttendanceSettingsService : IAttendanceSettingsService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AttendanceSettingsService> _logger;

    public AttendanceSettingsService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ILogger<AttendanceSettingsService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Tenant-default policy
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<AttendanceSettingsDto>> GetTenantSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AttendanceSettingsDto>.Failure("Tenant context is not resolved.", 400);

        // Predicated on LocationId == null via the resolver's read-only accessor: a GET must not create a
        // row as a side effect, and must never return a Location override as "the tenant policy".
        var entity = await AttendancePolicyResolver.GetTenantDefaultOrNullAsync(_dbContext, cancellationToken);

        return Result<AttendanceSettingsDto>.Success(
            entity is null ? new AttendanceSettingsDto() : MapToDto(entity));
    }

    public async Task<Result<AttendanceSettingsDto>> UpsertTenantSettingsAsync(
        AttendanceSettingsDto settings, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AttendanceSettingsDto>.Failure("Tenant context is not resolved.", 400);

        // ⚠ Predicated on LocationId == null. Without this predicate an existing Location override could be
        // fetched and OVERWRITTEN with the tenant-wide policy the admin just typed.
        var entity = await _dbContext.AttendanceSettings
            .FirstOrDefaultAsync(s => s.LocationId == null, cancellationToken);

        if (entity is null)
        {
            entity = new AttendanceSettings
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                LocationId = null,
            };
            _dbContext.AttendanceSettings.Add(entity);
        }

        Apply(entity, settings);

        var conflict = await SaveOrConflictAsync(
            "Another tenant attendance policy was created concurrently.", "settings_already_exists",
            cancellationToken);
        if (conflict is not null)
            return conflict;

        _logger.LogInformation(
            "Attendance settings upserted for tenant {TenantId} (TENANT DEFAULT scope). SettingsId={SettingsId}",
            _tenantContext.TenantId, entity.Id);

        return Result<AttendanceSettingsDto>.Success(MapToDto(entity));
    }

    // ══════════════════════════════════════════════════════════════
    //  Per-Location overrides
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<IReadOnlyList<AttendanceSettingsDto>>> GetOverridesAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<AttendanceSettingsDto>>.Failure("Tenant context is not resolved.", 400);

        // ⚠ Predicated on LocationId != null — the tenant-default row is NOT an override and must never
        // appear in this list. Left-joined to Locations (rather than projected through the navigation) so an
        // override survives in the list even if its location row went away, and so the projection does not
        // depend on the navigation's own query filter.
        var rows = await (
                from s in _dbContext.AttendanceSettings.AsNoTracking()
                where s.LocationId != null
                join l in _dbContext.Locations.AsNoTracking() on s.LocationId equals l.Id into joined
                from l in joined.DefaultIfEmpty()
                select new { Settings = s, LocationName = l != null ? l.Name : null })
            .ToListAsync(cancellationToken);

        var dtos = rows
            .Select(r => MapToDto(r.Settings, r.LocationName))
            .OrderBy(d => d.LocationName)
            .ToList();

        return Result<IReadOnlyList<AttendanceSettingsDto>>.Success(dtos);
    }

    public async Task<Result<AttendanceSettingsDto>> GetOverrideAsync(
        Guid locationId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AttendanceSettingsDto>.Failure("Tenant context is not resolved.", 400);

        // Existence check only (NOT active): an admin must still be able to read — and then delete — the
        // override of a location that has since been deactivated. A cross-tenant id is not found under the
        // global filter and lands here, so isolation and "unknown location" share one rejection path.
        var location = await _dbContext.Locations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == locationId, cancellationToken);
        if (location is null)
            return InvalidLocation<AttendanceSettingsDto>();

        // ⚠ Predicated on LocationId == locationId.
        var entity = await _dbContext.AttendanceSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.LocationId == locationId, cancellationToken);

        if (entity is null)
            return Result<AttendanceSettingsDto>.Failure(
                "This location has no attendance policy override; the tenant default applies.",
                404, "override_not_found");

        return Result<AttendanceSettingsDto>.Success(MapToDto(entity, location.Name));
    }

    public async Task<Result<AttendanceSettingsDto>> UpsertOverrideAsync(
        Guid locationId, AttendanceSettingsDto settings, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AttendanceSettingsDto>.Failure("Tenant context is not resolved.", 400);

        // DB-dependent check — belongs here, not in FluentValidation (no DB access there). The location must
        // exist, be same-tenant AND be active before we pin a policy to it. Runs under the EF global tenant
        // filter, so a cross-tenant id simply isn't found and takes this same rejection path.
        var location = await _dbContext.Locations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == locationId && l.IsActive, cancellationToken);
        if (location is null)
            return InvalidLocation<AttendanceSettingsDto>();

        // ⚠ Predicated on LocationId == locationId. Upsert UPDATES this location's own row — it must never
        // reach the tenant-default row (LocationId null) or another location's override.
        var entity = await _dbContext.AttendanceSettings
            .FirstOrDefaultAsync(s => s.LocationId == locationId, cancellationToken);

        if (entity is null)
        {
            // Deliberately NOT seeded from the tenant-default row: an override is the complete policy the
            // admin sent (row-level resolution, AC-3), not a delta on top of the tenant policy.
            entity = new AttendanceSettings
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                LocationId = locationId,
            };
            _dbContext.AttendanceSettings.Add(entity);
        }

        Apply(entity, settings);

        var conflict = await SaveOrConflictAsync(
            "An attendance policy override already exists for this location.", "override_already_exists",
            cancellationToken);
        if (conflict is not null)
            return conflict;

        _logger.LogInformation(
            "Attendance settings upserted for tenant {TenantId}, LOCATION OVERRIDE scope {LocationId}. "
            + "SettingsId={SettingsId}",
            _tenantContext.TenantId, locationId, entity.Id);

        return Result<AttendanceSettingsDto>.Success(MapToDto(entity, location.Name));
    }

    public async Task<Result> DeleteOverrideAsync(
        Guid locationId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var locationExists = await _dbContext.Locations.AsNoTracking()
            .AnyAsync(l => l.Id == locationId, cancellationToken);
        if (!locationExists)
            return Result.Failure(InvalidLocationMessage, 400, "invalid_location");

        // ⚠ Predicated on LocationId == locationId. An unpredicated read here would soft-delete an
        // ARBITRARY row — quite possibly the TENANT DEFAULT, silently resetting the whole tenant's policy.
        var entity = await _dbContext.AttendanceSettings
            .FirstOrDefaultAsync(s => s.LocationId == locationId, cancellationToken);

        if (entity is null)
            return Result.Failure(
                "This location has no attendance policy override; the tenant default applies.",
                404, "override_not_found");

        // Soft delete: both the query filter and ix_attendance_settings_tenant_location_unique are
        // predicated on is_deleted = false, so the location's employees fall back to the tenant default and
        // a fresh override can be created later without colliding with this row.
        entity.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Attendance settings override removed for tenant {TenantId}, location {LocationId}; that "
            + "location's employees now fall back to the tenant default. SettingsId={SettingsId}",
            _tenantContext.TenantId, locationId, entity.Id);

        return Result.Success();
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private const string InvalidLocationMessage = "Location not found or is not active.";

    private static Result<T> InvalidLocation<T>()
        => Result<T>.Failure(InvalidLocationMessage, 400, "invalid_location");

    /// <summary>
    /// Saves, translating a unique-index violation (23505 on ix_attendance_settings_tenant_location_unique
    /// or ..._tenant_default_unique) into a 409 instead of letting it surface as a 500. The read-then-write
    /// upsert above already UPDATEs an existing row; this only fires when a concurrent insert wins the race
    /// between our read and our write.
    /// </summary>
    private async Task<Result<AttendanceSettingsDto>?> SaveOrConflictAsync(
        string message, string errorCode, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException ex) when ((ex.InnerException as PostgresException)?.SqlState == "23505")
        {
            _logger.LogWarning(
                ex, "Concurrent attendance-settings insert for tenant {TenantId} lost the race ({ErrorCode}).",
                _tenantContext.TenantId, errorCode);
            return Result<AttendanceSettingsDto>.Failure(message, 409, errorCode);
        }
    }

    /// <summary>
    /// FULL REPLACE of the scope's policy (BUG-117 class): every one of the 25 fields is applied as sent, so
    /// a field omitted from the JSON body takes the DTO default and RESETS that setting. LocationId is NOT
    /// assigned here — the scope comes from the route and is fixed at row creation.
    /// </summary>
    private static void Apply(AttendanceSettings entity, AttendanceSettingsDto dto)
    {
        entity.RequireGeolocation = dto.RequireGeolocation;
        entity.GeoFenceEnabled = dto.GeoFenceEnabled;
        entity.GeoFenceLatitude = dto.GeoFenceLatitude;
        entity.GeoFenceLongitude = dto.GeoFenceLongitude;
        entity.GeoFenceRadiusMeters = dto.GeoFenceRadiusMeters;
        entity.IpAllowlistEnabled = dto.IpAllowlistEnabled;
        entity.IpAllowlist = dto.IpAllowlist is null ? new List<string>() : new List<string>(dto.IpAllowlist);
        entity.RequirePhoto = dto.RequirePhoto;
        entity.GracePeriodMinutes = dto.GracePeriodMinutes;
        entity.StandardWorkMinutes = dto.StandardWorkMinutes;
        entity.MinimumWorkMinutes = dto.MinimumWorkMinutes;
        entity.AutoBreakMinutes = dto.AutoBreakMinutes;
        entity.AutoBreakThresholdMinutes = dto.AutoBreakThresholdMinutes;
        entity.OvertimeThresholdMinutes = dto.OvertimeThresholdMinutes;
        entity.RegularizationLookbackDays = dto.RegularizationLookbackDays;
        entity.OvertimeMinimumThresholdMinutes = dto.OvertimeMinimumThresholdMinutes;
        entity.WeekdayOvertimeMultiplier = dto.WeekdayOvertimeMultiplier;
        entity.WeekendOvertimeMultiplier = dto.WeekendOvertimeMultiplier;
        entity.HolidayOvertimeMultiplier = dto.HolidayOvertimeMultiplier;
        entity.MaxDailyOvertimeMinutes = dto.MaxDailyOvertimeMinutes;
        entity.MaxWeeklyOvertimeMinutes = dto.MaxWeeklyOvertimeMinutes;
        entity.RequireOvertimePreApproval = dto.RequireOvertimePreApproval;
        entity.FteScaledOvertimeBase = dto.FteScaledOvertimeBase;
        entity.HalfDayEnabled = dto.HalfDayEnabled;
        entity.AbsenteeismThresholdDays = dto.AbsenteeismThresholdDays;
    }

    private static AttendanceSettingsDto MapToDto(AttendanceSettings e, string? locationName = null) => new()
    {
        LocationId = e.LocationId,
        LocationName = locationName,
        RequireGeolocation = e.RequireGeolocation,
        GeoFenceEnabled = e.GeoFenceEnabled,
        GeoFenceLatitude = e.GeoFenceLatitude,
        GeoFenceLongitude = e.GeoFenceLongitude,
        GeoFenceRadiusMeters = e.GeoFenceRadiusMeters,
        IpAllowlistEnabled = e.IpAllowlistEnabled,
        IpAllowlist = new List<string>(e.IpAllowlist),
        RequirePhoto = e.RequirePhoto,
        GracePeriodMinutes = e.GracePeriodMinutes,
        StandardWorkMinutes = e.StandardWorkMinutes,
        MinimumWorkMinutes = e.MinimumWorkMinutes,
        AutoBreakMinutes = e.AutoBreakMinutes,
        AutoBreakThresholdMinutes = e.AutoBreakThresholdMinutes,
        OvertimeThresholdMinutes = e.OvertimeThresholdMinutes,
        RegularizationLookbackDays = e.RegularizationLookbackDays,
        OvertimeMinimumThresholdMinutes = e.OvertimeMinimumThresholdMinutes,
        WeekdayOvertimeMultiplier = e.WeekdayOvertimeMultiplier,
        WeekendOvertimeMultiplier = e.WeekendOvertimeMultiplier,
        HolidayOvertimeMultiplier = e.HolidayOvertimeMultiplier,
        MaxDailyOvertimeMinutes = e.MaxDailyOvertimeMinutes,
        MaxWeeklyOvertimeMinutes = e.MaxWeeklyOvertimeMinutes,
        RequireOvertimePreApproval = e.RequireOvertimePreApproval,
        FteScaledOvertimeBase = e.FteScaledOvertimeBase,
        HalfDayEnabled = e.HalfDayEnabled,
        AbsenteeismThresholdDays = e.AbsenteeismThresholdDays,
    };
}
