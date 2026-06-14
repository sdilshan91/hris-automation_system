using System.Text.Json;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Employee attendance punches (US-ATT-001).
/// All queries are tenant-scoped via ITenantContext and EF global query filters. Tenant isolation
/// (NFR-2 in the story asks for PostgreSQL RLS, which this codebase does NOT use) is enforced via
/// the global query filter + TenantInterceptor instead. Dashboard caching (FR-6, Redis) is deferred.
/// </summary>
public sealed class AttendanceService : IAttendanceService
{
    private const string OperationName = "ClockIn";

    // NFR-4: idempotency window for double-click protection.
    private static readonly TimeSpan IdempotencyWindow = TimeSpan.FromSeconds(5);

    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AttendanceService> _logger;

    public AttendanceService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<AttendanceService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<AttendanceLogDto>> ClockInAsync(
        ClockInData data, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AttendanceLogDto>.Failure("Tenant context is not resolved.", 400);

        // NFR-4: short-window idempotency. A replay within the window returns the original response.
        if (!string.IsNullOrWhiteSpace(data.IdempotencyKey))
        {
            var existing = await _dbContext.IdempotencyRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.IdempotencyKey == data.IdempotencyKey && r.OperationName == OperationName,
                    cancellationToken);

            if (existing is not null && existing.ResponseJson is not null)
            {
                var cached = JsonSerializer.Deserialize<AttendanceLogDto>(existing.ResponseJson);
                if (cached is not null)
                    return Result<AttendanceLogDto>.Success(cached);
            }
        }

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<AttendanceLogDto>.Failure(
                "No employee record is linked to the current user.", 403);

        // BR-5: only active employees may clock in.
        if (employee.Status is EmployeeStatus.Terminated or EmployeeStatus.Inactive)
            return Result<AttendanceLogDto>.Failure(
                "Clock-in is not allowed for your current employment status.", 403);

        // BR-1 / FR-2 / AC-2: at most one open (un-clocked-out) record at any time.
        var hasOpen = await _dbContext.AttendanceLogs
            .AnyAsync(a => a.EmployeeId == employee.Id && a.ClockOut == null, cancellationToken);
        if (hasOpen)
            return Result<AttendanceLogDto>.Failure(
                "You have already clocked in. Please clock out first.", 409, "already_clocked_in");

        var settings = await GetOrCreateSettingsAsync(cancellationToken);

        // AC-5 / FR-4: IP allowlist enforcement.
        if (settings.IpAllowlistEnabled)
        {
            var ip = data.IpAddress;
            if (string.IsNullOrWhiteSpace(ip) || !settings.IpAllowlist.Contains(ip))
                return Result<AttendanceLogDto>.Failure(
                    "Clock-in is only allowed from authorized network locations.", 403, "ip_not_allowed");
        }

        var hasCoordinates = data.Latitude.HasValue && data.Longitude.HasValue;

        // AC-3 / BR-2: geolocation required but missing.
        if (settings.RequireGeolocation && !hasCoordinates)
            return Result<AttendanceLogDto>.Failure(
                "Location is required to clock in. Please enable location access and try again.", 400);

        // FR-3: geo-fence radius check when enabled and coordinates were supplied.
        if (settings.GeoFenceEnabled && hasCoordinates
            && settings.GeoFenceLatitude.HasValue && settings.GeoFenceLongitude.HasValue)
        {
            var distance = HaversineMeters(
                (double)settings.GeoFenceLatitude.Value, (double)settings.GeoFenceLongitude.Value,
                (double)data.Latitude!.Value, (double)data.Longitude!.Value);

            if (distance > settings.GeoFenceRadiusMeters)
                return Result<AttendanceLogDto>.Failure(
                    "You are outside the allowed clock-in area.", 403, "geo_fence_violation");
        }

        // BR-6: selfie photo required.
        if (settings.RequirePhoto && string.IsNullOrWhiteSpace(data.PhotoUrl))
            return Result<AttendanceLogDto>.Failure(
                "A photo is required to clock in.", 400);

        var source = string.IsNullOrWhiteSpace(data.Source) ? "WEB" : data.Source!;

        var log = new AttendanceLog
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employee.Id,
            ClockIn = DateTime.UtcNow,          // FR-1 / FR-7: store in UTC.
            ClockInLatitude = hasCoordinates ? data.Latitude : null,
            ClockInLongitude = hasCoordinates ? data.Longitude : null,
            ClockInIp = data.IpAddress,          // FR-5.
            ClockInUserAgent = data.UserAgent,   // FR-5.
            ClockInPhotoUrl = string.IsNullOrWhiteSpace(data.PhotoUrl) ? null : data.PhotoUrl,
            Source = source,
        };

        _dbContext.AttendanceLogs.Add(log);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var dto = MapToDto(log);

        // NFR-4: record the idempotency key so a replay within the window is a no-op.
        if (!string.IsNullOrWhiteSpace(data.IdempotencyKey))
            await TryRecordIdempotencyAsync(data.IdempotencyKey!, dto, cancellationToken);

        _logger.LogInformation(
            "Employee {EmployeeId} clocked in (attendance_log {LogId}) for tenant {TenantId}.",
            employee.Id, log.Id, _tenantContext.TenantId);

        return Result<AttendanceLogDto>.Success(dto);
    }

    public async Task<Result<ClockOutResultDto>> ClockOutAsync(
        ClockOutData data, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ClockOutResultDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<ClockOutResultDto>.Failure(
                "No employee record is linked to the current user.", 403);

        // BR-1 / AC-2: clock-out requires an open (un-clocked-out) record.
        var openLog = await _dbContext.AttendanceLogs
            .Where(a => a.EmployeeId == employee.Id && a.ClockOut == null)
            .OrderByDescending(a => a.ClockIn)
            .FirstOrDefaultAsync(cancellationToken);
        if (openLog is null)
            return Result<ClockOutResultDto>.Failure(
                "No active clock-in found. Please clock in first or submit a regularization request.",
                404, "no_active_clock_in");

        var settings = await GetOrCreateSettingsAsync(cancellationToken);

        var hasCoordinates = data.Latitude.HasValue && data.Longitude.HasValue;

        // AC-5 / FR-6: geolocation required by tenant policy but missing on clock-out.
        if (settings.RequireGeolocation && !hasCoordinates)
            return Result<ClockOutResultDto>.Failure(
                "Location is required to clock out. Please enable location access and try again.", 400);

        var clockOut = DateTime.UtcNow;                  // FR-1: server-side UTC, not client-reported.

        // FR-2/FR-3/FR-4/FR-7, BR-2/BR-3/BR-4/BR-6: minute-accurate work-hours calculation.
        var calc = AttendanceCalculator.Calculate(openLog.ClockIn, clockOut, settings);

        openLog.ClockOut = clockOut;
        openLog.ClockOutLatitude = hasCoordinates ? data.Latitude : null;
        openLog.ClockOutLongitude = hasCoordinates ? data.Longitude : null;
        openLog.ClockOutIp = data.IpAddress;
        openLog.TotalWorkMinutes = calc.TotalWorkMinutes;
        openLog.OvertimeMinutes = calc.OvertimeMinutes;
        openLog.Status = calc.Status;

        // NFR-3: single atomic SaveChanges; the AuditInterceptor stamps updated_at/updated_by.
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Employee {EmployeeId} clocked out (attendance_log {LogId}, {TotalMinutes} min, status {Status}) for tenant {TenantId}.",
            employee.Id, openLog.Id, calc.TotalWorkMinutes, calc.Status, _tenantContext.TenantId);

        return Result<ClockOutResultDto>.Success(MapToResult(openLog));
    }

    public async Task<Result<ClockStatusDto>> GetClockStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ClockStatusDto>.Failure("Tenant context is not resolved.", 400);

        var employee = await _dbContext.Employees
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == _currentUser.UserId, cancellationToken);
        if (employee is null)
            return Result<ClockStatusDto>.Failure(
                "No employee record is linked to the current user.", 403);

        // BR-1: the open (un-clocked-out) record, if any, drives IsClockedIn / ClockedInAt.
        var openLog = await _dbContext.AttendanceLogs
            .AsNoTracking()
            .Where(a => a.EmployeeId == employee.Id && a.ClockOut == null)
            .OrderByDescending(a => a.ClockIn)
            .FirstOrDefaultAsync(cancellationToken);

        // BR-2: read-only — default RequireGeolocation to false when no settings row exists.
        // Unlike clock-in, status must not create a settings row as a side effect.
        var requireGeolocation = await _dbContext.AttendanceSettings
            .AsNoTracking()
            .Select(x => x.RequireGeolocation)
            .FirstOrDefaultAsync(cancellationToken);

        // US-ATT-002: the most-recently-closed session, so the dashboard can re-render the summary
        // card after a reload. Independent of the open record above.
        var lastClosed = await _dbContext.AttendanceLogs
            .AsNoTracking()
            .Where(a => a.EmployeeId == employee.Id && a.ClockOut != null)
            .OrderByDescending(a => a.ClockOut)
            .FirstOrDefaultAsync(cancellationToken);

        var dto = new ClockStatusDto
        {
            IsClockedIn = openLog is not null,
            ClockedInAt = openLog?.ClockIn,
            RequireGeolocation = requireGeolocation,
            ShiftName = null,    // US-ATT-005 not built yet.
            ShiftStart = null,   // US-ATT-005 not built yet.
            LastCompleted = lastClosed is null ? null : MapToResult(lastClosed),
        };

        return Result<ClockStatusDto>.Success(dto);
    }

    /// <summary>
    /// Returns the tenant's attendance settings, creating a default (all enforcement off) row if none
    /// exists. The TenantInterceptor stamps TenantId on the new row; we set it explicitly for clarity.
    /// </summary>
    private async Task<AttendanceSettings> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _dbContext.AttendanceSettings
            .FirstOrDefaultAsync(cancellationToken);
        if (settings is not null)
            return settings;

        settings = new AttendanceSettings
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
        };
        _dbContext.AttendanceSettings.Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private async Task TryRecordIdempotencyAsync(
        string key, AttendanceLogDto dto, CancellationToken cancellationToken)
    {
        try
        {
            _dbContext.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                IdempotencyKey = key,
                OperationName = OperationName,
                ResponseJson = JsonSerializer.Serialize(dto),
                ResponseStatusCode = 201,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(IdempotencyWindow),
            });
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request with the same key already wrote the record (unique index).
            // The clock-in itself succeeded; the duplicate idempotency row is harmless to drop.
            _logger.LogDebug("Idempotency key {Key} already recorded for {Operation}.", key, OperationName);
        }
    }

    private static AttendanceLogDto MapToDto(AttendanceLog log) => new()
    {
        Id = log.Id,
        EmployeeId = log.EmployeeId,
        ClockIn = log.ClockIn,
        ClockOut = log.ClockOut,
        ClockInLatitude = log.ClockInLatitude,
        ClockInLongitude = log.ClockInLongitude,
        Source = log.Source,
        CreatedAt = log.CreatedAt,
    };

    /// <summary>
    /// Maps a CLOSED attendance log to the clock-out summary DTO (US-ATT-002). Assumes
    /// <see cref="AttendanceLog.ClockOut"/> and the computed fields are set; coalesces defensively.
    /// </summary>
    private static ClockOutResultDto MapToResult(AttendanceLog log) => new()
    {
        Id = log.Id,
        EmployeeId = log.EmployeeId,
        ClockIn = log.ClockIn,
        ClockOut = log.ClockOut!.Value,
        TotalWorkMinutes = log.TotalWorkMinutes ?? 0,
        OvertimeMinutes = log.OvertimeMinutes ?? 0,
        Status = log.Status ?? "COMPLETE",
    };

    /// <summary>
    /// Great-circle distance in metres between two lat/long points (FR-3 geo-fence check).
    /// </summary>
    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusM = 6_371_000d;
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return earthRadiusM * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;
}
