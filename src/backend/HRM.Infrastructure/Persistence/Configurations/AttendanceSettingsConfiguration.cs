using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the AttendanceSettings entity (US-ATT-001 BR-2/BR-3/BR-6).
/// Maps to the "attendance_settings" table; exactly one row per tenant.
/// </summary>
public sealed class AttendanceSettingsConfiguration : IEntityTypeConfiguration<AttendanceSettings>
{
    public void Configure(EntityTypeBuilder<AttendanceSettings> builder)
    {
        builder.ToTable("attendance_settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id");

        builder.Property(s => s.RequireGeolocation)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(s => s.GeoFenceEnabled)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(s => s.GeoFenceLatitude)
            .HasColumnType("numeric(10,7)");

        builder.Property(s => s.GeoFenceLongitude)
            .HasColumnType("numeric(10,7)");

        builder.Property(s => s.GeoFenceRadiusMeters)
            .HasDefaultValue(100)
            .IsRequired();

        builder.Property(s => s.IpAllowlistEnabled)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(s => s.IpAllowlist)
            .HasColumnType("text[]");

        builder.Property(s => s.RequirePhoto)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(s => s.GracePeriodMinutes)
            .HasDefaultValue(0)
            .IsRequired();

        // US-ATT-002 work-hours calculation policy (tenant-level fallback until US-ATT-005).
        builder.Property(s => s.StandardWorkMinutes)
            .HasDefaultValue(480)
            .IsRequired();

        builder.Property(s => s.MinimumWorkMinutes)
            .HasDefaultValue(240)
            .IsRequired();

        builder.Property(s => s.AutoBreakMinutes)
            .HasDefaultValue(60)
            .IsRequired();

        builder.Property(s => s.AutoBreakThresholdMinutes)
            .HasDefaultValue(360)
            .IsRequired();

        builder.Property(s => s.OvertimeThresholdMinutes)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // One settings row per tenant.
        builder.HasIndex(s => s.TenantId)
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_attendance_settings_tenant_unique");
    }
}
