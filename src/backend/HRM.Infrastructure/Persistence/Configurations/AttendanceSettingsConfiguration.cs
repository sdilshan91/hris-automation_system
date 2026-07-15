using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the AttendanceSettings entity (US-ATT-001 BR-2/BR-3/BR-6).
/// Maps to the "attendance_settings" table.
///
/// <para>⚠ <b>NOT "exactly one row per tenant" any more (CAL-4 / US-ATT-011 AC-3).</b> One row per
/// (tenant, location): the row with a NULL LocationId is the tenant default, each Location override is its
/// own row. Reads must go through <c>AttendancePolicyResolver</c> or filter <c>LocationId</c> explicitly —
/// an unpredicated read returns an arbitrary row.</para>
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

        // US-ATT-003 regularization lookback window (FR-6/BR-2).
        builder.Property(s => s.RegularizationLookbackDays)
            .HasDefaultValue(7)
            .IsRequired();

        // US-ATT-006 overtime rules (FR-3). Tenant-configurable thresholds, multipliers, caps.
        builder.Property(s => s.OvertimeMinimumThresholdMinutes)
            .HasDefaultValue(30)
            .IsRequired();

        builder.Property(s => s.WeekdayOvertimeMultiplier)
            .HasColumnType("numeric(3,2)")
            .HasDefaultValue(1.5m)
            .IsRequired();

        builder.Property(s => s.WeekendOvertimeMultiplier)
            .HasColumnType("numeric(3,2)")
            .HasDefaultValue(2.0m)
            .IsRequired();

        builder.Property(s => s.HolidayOvertimeMultiplier)
            .HasColumnType("numeric(3,2)")
            .HasDefaultValue(2.5m)
            .IsRequired();

        builder.Property(s => s.MaxDailyOvertimeMinutes)
            .HasDefaultValue(240)
            .IsRequired();

        builder.Property(s => s.MaxWeeklyOvertimeMinutes)
            .HasDefaultValue(1200)
            .IsRequired();

        builder.Property(s => s.RequireOvertimePreApproval)
            .HasDefaultValue(false)
            .IsRequired();

        // US-ATT-011 AC-5 / US-CHR-013: FTE-scaled overtime hourly base. Default FALSE — off means the OT
        // base is byte-identical to its pre-US-CHR-013 value for every existing tenant.
        builder.Property(s => s.FteScaledOvertimeBase)
            .HasDefaultValue(false)
            .IsRequired();

        // US-LV-011 absenteeism report threshold (BR-4) — unplanned LOP days per month.
        builder.Property(s => s.AbsenteeismThresholdDays)
            .HasColumnType("numeric(5,2)")
            .HasDefaultValue(3m)
            .IsRequired();

        builder.Property(s => s.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // US-ATT-011 AC-3: at most ONE row per (tenant, location) — the tenant default is the row whose
        // LocationId is null, each Location override is its own row. This REPLACES the old
        // ix_attendance_settings_tenant_unique (unique on TenantId alone), which structurally allowed only
        // one row per tenant.
        //
        // ⚠ Postgres treats NULLs as distinct in a unique index, so (TenantId, NULL) would NOT be
        // de-duplicated by this index alone — two tenant-default rows could be inserted and the
        // "tenant default" would become ambiguous. The second, partial index below closes that hole by
        // enforcing uniqueness of the tenant-default row explicitly.
        builder.HasIndex(s => new { s.TenantId, s.LocationId })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_attendance_settings_tenant_location_unique");

        builder.HasIndex(s => s.TenantId)
            .IsUnique()
            .HasFilter("is_deleted = false AND location_id IS NULL")
            .HasDatabaseName("ix_attendance_settings_tenant_default_unique");

        // Restrict: a Location with a policy override must not be deletable out from under it. (Note the
        // BUG-287 lesson — Restrict only stops a HARD delete; a soft delete is an UPDATE no FK can block.)
        builder.HasOne(s => s.Location)
            .WithMany()
            .HasForeignKey(s => s.LocationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
