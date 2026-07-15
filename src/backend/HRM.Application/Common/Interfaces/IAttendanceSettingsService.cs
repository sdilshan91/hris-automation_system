using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// CAL-4b / US-ATT-011 AC-3: the ADMIN write path for <c>AttendanceSettings</c> — the tenant-default
/// attendance policy and its per-Location overrides. Reading the EFFECTIVE policy for an employee is a
/// different concern and belongs to <c>AttendancePolicyResolver</c>; this service is the configuration
/// surface a Tenant Admin drives (gated by Attendance.ConfigurePolicy at the controller).
///
/// <para>⚠ <b>One row per (tenant, location).</b> A row with a null LocationId is the tenant default; a
/// row with a set LocationId is that Location's override. Every read and write in the implementation is
/// explicitly predicated on LocationId — an unpredicated <c>FirstOrDefaultAsync()</c> would return an
/// ARBITRARY row and could apply one branch's geofence/overtime policy tenant-wide.</para>
///
/// <para>⚠ <b>Upserts are a FULL REPLACE of that scope's policy (BUG-117 class)</b> — see
/// <see cref="AttendanceSettingsDto"/>. The client contract is GET-then-PUT.</para>
///
/// <para>Tenant isolation is the EF global query filter + TenantInterceptor. A cross-tenant locationId is
/// simply not found under the filter and is rejected as "invalid_location" — never leaked.</para>
/// </summary>
public interface IAttendanceSettingsService
{
    /// <summary>
    /// The TENANT-DEFAULT policy (the row whose LocationId is null), or code defaults when the tenant has
    /// never configured one. Read-only: never creates a row as a side effect.
    /// </summary>
    Task<Result<AttendanceSettingsDto>> GetTenantSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Full-replace upsert of the TENANT-DEFAULT policy: updates the existing LocationId-null row, or
    /// creates it when absent. Never touches a Location override.
    /// </summary>
    Task<Result<AttendanceSettingsDto>> UpsertTenantSettingsAsync(
        AttendanceSettingsDto settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every Location override configured for the tenant (LocationId set), each carrying its LocationId and
    /// LocationName. The tenant-default row is excluded. Empty when no overrides exist.
    /// </summary>
    Task<Result<IReadOnlyList<AttendanceSettingsDto>>> GetOverridesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One Location's override. Fails 400 "invalid_location" when the location does not exist in this
    /// tenant, and 404 "override_not_found" when the location exists but has no override.
    /// </summary>
    Task<Result<AttendanceSettingsDto>> GetOverrideAsync(
        Guid locationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Full-replace upsert of ONE Location's override: updates that location's existing row, or creates it
    /// when absent. Does NOT copy the tenant-default row — the caller sends the complete policy — and never
    /// mutates the tenant-default row. Fails 400 "invalid_location" when the location does not exist in
    /// this tenant or is inactive; 409 "override_already_exists" when a concurrent insert wins the race.
    /// </summary>
    Task<Result<AttendanceSettingsDto>> UpsertOverrideAsync(
        Guid locationId, AttendanceSettingsDto settings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one Location's override; that Location's employees then fall back to the tenant default.
    /// Fails 400 "invalid_location" when the location does not exist in this tenant, 404
    /// "override_not_found" when it has no override.
    /// </summary>
    Task<Result> DeleteOverrideAsync(Guid locationId, CancellationToken cancellationToken = default);
}
