namespace HRM.Domain.Entities;

/// <summary>
/// DF-23 / ISSUE-068: one allowed clock-in location on an <see cref="AttendanceSettings"/> row (the
/// multi-location geofence). A settings row (tenant-default when <c>LocationId</c> is null, per-location
/// override otherwise) may carry MANY allowed locations; a clock-in passes the geofence when the punch is
/// within ANY one allowed location's own radius. Tenant-scoped via <see cref="BaseEntity.TenantId"/>.
/// Maps to the "attendance_geofence_locations" table.
///
/// <para><b>Backward-compat.</b> A settings row with NO allowed-location children falls back to the legacy
/// single-center scalars (<see cref="AttendanceSettings.GeoFenceLatitude"/> etc.), so a single-center tenant
/// behaves byte-identically to its pre-DF-23 value. The migration backfills one 'Primary' row per existing
/// enabled single-center settings row, so those tenants land on the multi-location path without a behaviour
/// change.</para>
/// </summary>
public sealed class GeofenceLocation : BaseEntity
{
    /// <summary>FK to the owning <see cref="AttendanceSettings"/> row whose allowed locations this is one of.</summary>
    public Guid AttendanceSettingsId { get; set; }

    /// <summary>Human label for this allowed location, e.g. "HQ" or "Warehouse". Max 100 chars.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Allowed-location latitude, [-90, 90].</summary>
    public decimal Latitude { get; set; }

    /// <summary>Allowed-location longitude, [-180, 180].</summary>
    public decimal Longitude { get; set; }

    /// <summary>Radius in metres around this location within which clock-in is permitted.</summary>
    public int RadiusMeters { get; set; }
}
