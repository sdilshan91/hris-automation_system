namespace HRM.Domain.Entities;

/// <summary>
/// Per-tenant attendance policy that drives clock-in enforcement (US-ATT-001 BR-2/BR-3/BR-6).
/// Exactly one row per tenant; created lazily with safe defaults (all enforcement off) the first
/// time a tenant clocks in if no row exists yet. Tenant-scoped via <see cref="BaseEntity.TenantId"/>.
/// Maps to the "attendance_settings" table.
/// </summary>
public sealed class AttendanceSettings : BaseEntity
{
    /// <summary>
    /// BR-2 / AC-3: when true, latitude+longitude are mandatory on clock-in and a clock-in
    /// without coordinates is rejected. When false, geolocation is optional (AC-4).
    /// </summary>
    public bool RequireGeolocation { get; set; }

    /// <summary>
    /// FR-3: when true, supplied coordinates are validated against the configured allowed
    /// location within <see cref="GeoFenceRadiusMeters"/>.
    /// </summary>
    public bool GeoFenceEnabled { get; set; }

    /// <summary>
    /// Allowed-location latitude used for the geo-fence check (FR-3). Null when not configured.
    /// </summary>
    public decimal? GeoFenceLatitude { get; set; }

    /// <summary>
    /// Allowed-location longitude used for the geo-fence check (FR-3). Null when not configured.
    /// </summary>
    public decimal? GeoFenceLongitude { get; set; }

    /// <summary>
    /// Radius in metres around the allowed location within which clock-in is permitted (FR-3).
    /// </summary>
    public int GeoFenceRadiusMeters { get; set; } = 100;

    /// <summary>
    /// BR-3 / AC-5: when true, the request source IP must be in <see cref="IpAllowlist"/>.
    /// </summary>
    public bool IpAllowlistEnabled { get; set; }

    /// <summary>
    /// Allowed source IP addresses (exact match) when <see cref="IpAllowlistEnabled"/> is true (FR-4).
    /// Stored as a PostgreSQL text[] column.
    /// </summary>
    public List<string> IpAllowlist { get; set; } = new();

    /// <summary>
    /// BR-6: when true, a selfie photo must be supplied before the clock-in is accepted.
    /// </summary>
    public bool RequirePhoto { get; set; }

    /// <summary>
    /// BR-4: grace period (minutes) after shift start within which a clock-in is not marked late.
    /// Stored for use by later lateness stories; not enforced in US-ATT-001.
    /// </summary>
    public int GracePeriodMinutes { get; set; }
}
