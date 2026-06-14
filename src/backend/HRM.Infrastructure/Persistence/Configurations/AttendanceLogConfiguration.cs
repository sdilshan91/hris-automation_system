using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the AttendanceLog entity (US-ATT-001 §7).
/// Maps to the "attendance_log" table with snake_case naming convention.
/// </summary>
public sealed class AttendanceLogConfiguration : IEntityTypeConfiguration<AttendanceLog>
{
    public void Configure(EntityTypeBuilder<AttendanceLog> builder)
    {
        builder.ToTable("attendance_log");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id");

        builder.Property(a => a.EmployeeId)
            .IsRequired();

        builder.Property(a => a.ClockIn)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(a => a.ClockOut)
            .HasColumnType("timestamptz");

        builder.Property(a => a.ClockInLatitude)
            .HasColumnType("numeric(10,7)");

        builder.Property(a => a.ClockInLongitude)
            .HasColumnType("numeric(10,7)");

        builder.Property(a => a.ClockInIp)
            .HasMaxLength(50);

        builder.Property(a => a.ClockInUserAgent)
            .HasMaxLength(500);

        builder.Property(a => a.ClockInPhotoUrl)
            .HasMaxLength(500);

        builder.Property(a => a.Source)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Relationships ──────────────────────────────────────────

        builder.HasOne(a => a.Employee)
            .WithMany()
            .HasForeignKey(a => a.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Indexes (§7) ───────────────────────────────────────────

        // Lookup for an employee's punches (duplicate-detection / day scans).
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId, a.ClockIn })
            .HasDatabaseName("ix_attendance_log_tenant_emp_clockin");

        // BR-1 / FR-2: at most one OPEN (un-clocked-out) record per employee per tenant.
        // Partial unique index over open, non-deleted records.
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId })
            .IsUnique()
            .HasFilter("clock_out IS NULL AND is_deleted = false")
            .HasDatabaseName("ix_attendance_log_open_unique");
    }
}
