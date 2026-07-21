using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// DF-23 / ISSUE-068: EF Core configuration for <see cref="GeofenceLocation"/> — one allowed clock-in
/// location owned by an <see cref="AttendanceSettings"/> row (the multi-location geofence). Maps to the
/// "attendance_geofence_locations" table. Lat/lng columns mirror <c>AttendanceSettingsConfiguration</c>'s
/// GeoFence numeric(10,7) precision so a backfilled center is stored identically.
/// </summary>
public sealed class GeofenceLocationConfiguration : IEntityTypeConfiguration<GeofenceLocation>
{
    public void Configure(EntityTypeBuilder<GeofenceLocation> builder)
    {
        builder.ToTable("attendance_geofence_locations");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.AttendanceSettingsId)
            .IsRequired();

        builder.Property(g => g.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(g => g.Latitude)
            .HasColumnType("numeric(10,7)")
            .IsRequired();

        builder.Property(g => g.Longitude)
            .HasColumnType("numeric(10,7)")
            .IsRequired();

        builder.Property(g => g.RadiusMeters)
            .IsRequired();

        builder.Property(g => g.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasIndex(g => g.AttendanceSettingsId)
            .HasDatabaseName("ix_attendance_geofence_locations_attendance_settings_id");
    }
}
