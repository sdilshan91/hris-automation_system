using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the Holiday entity (US-LV-007).
/// Maps to the "holiday" table with snake_case naming convention.
/// </summary>
public sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.ToTable("holiday");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .HasColumnName("id");

        builder.Property(h => h.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(h => h.Date)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(h => h.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(h => h.LocationId);

        builder.Property(h => h.Description)
            .HasColumnType("text");

        builder.Property(h => h.IsRecurring)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(h => h.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(h => h.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // FK to Location (optional). ON DELETE SET NULL — when a location is deleted,
        // its holidays become tenant-wide rather than cascading away (mirrors the
        // nullable location FK pattern on Employee).
        builder.HasOne(h => h.Location)
            .WithMany()
            .HasForeignKey(h => h.LocationId)
            .OnDelete(DeleteBehavior.SetNull);

        // §7 / FR-6: range-query index for "holidays between {from} and {to}".
        builder.HasIndex(h => new { h.TenantId, h.Date })
            .HasDatabaseName("ix_holiday_tenant_id_date");

        // BR-1: a holiday date must be unique per tenant + location.
        // PostgreSQL treats NULLs as distinct in a unique index, so a single
        // (tenant_id, date, location_id) unique index would NOT stop two tenant-wide
        // (location_id IS NULL) holidays on the same date. We therefore split BR-1 into
        // two partial unique indexes (mirroring how LeaveType used a partial index for
        // soft-delete-aware uniqueness):
        //   1. location-specific rows: unique (tenant_id, date, location_id) where the
        //      location is set and the row is not soft-deleted.
        builder.HasIndex(h => new { h.TenantId, h.Date, h.LocationId })
            .IsUnique()
            .HasFilter("location_id IS NOT NULL AND is_deleted = false")
            .HasDatabaseName("ix_holiday_tenant_date_location_unique");

        //   2. tenant-wide rows (no location): unique (tenant_id, date) where the
        //      location is null and the row is not soft-deleted.
        builder.HasIndex(h => new { h.TenantId, h.Date })
            .IsUnique()
            .HasFilter("location_id IS NULL AND is_deleted = false")
            .HasDatabaseName("ix_holiday_tenant_date_nolocation_unique");
    }
}
