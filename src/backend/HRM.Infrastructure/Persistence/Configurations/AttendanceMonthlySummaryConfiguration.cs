using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the AttendanceMonthlySummary entity (US-ATT-007 §7).
/// Maps to the "attendance_monthly_summary" table with snake_case naming convention. One row per
/// (tenant, employee, year_month) — enforced by a partial unique index (soft-delete aware).
/// </summary>
public sealed class AttendanceMonthlySummaryConfiguration : IEntityTypeConfiguration<AttendanceMonthlySummary>
{
    public void Configure(EntityTypeBuilder<AttendanceMonthlySummary> builder)
    {
        builder.ToTable("attendance_monthly_summary");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id");

        builder.Property(s => s.EmployeeId)
            .IsRequired();

        builder.Property(s => s.YearMonth)
            .HasMaxLength(7)
            .IsRequired();

        builder.Property(s => s.TotalPresentDays)
            .HasColumnType("numeric(4,1)")
            .IsRequired();

        builder.Property(s => s.TotalAbsentDays)
            .HasColumnType("numeric(4,1)")
            .IsRequired();

        builder.Property(s => s.TotalLateCount)
            .IsRequired();

        builder.Property(s => s.TotalEarlyDepartureCount)
            .IsRequired();

        builder.Property(s => s.TotalWorkMinutes)
            .IsRequired();

        builder.Property(s => s.TotalOvertimeMinutes)
            .IsRequired();

        builder.Property(s => s.TotalLeaveDays)
            .HasColumnType("numeric(4,1)")
            .IsRequired();

        builder.Property(s => s.TotalHolidays)
            .IsRequired();

        builder.Property(s => s.TotalWeeklyOffs)
            .IsRequired();

        builder.Property(s => s.LopDays)
            .HasColumnType("numeric(4,1)")
            .IsRequired();

        builder.Property(s => s.GeneratedAt)
            .IsRequired();

        builder.Property(s => s.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Relationships ──────────────────────────────────────────
        builder.HasOne(s => s.Employee)
            .WithMany()
            .HasForeignKey(s => s.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Indexes (§7) ───────────────────────────────────────────

        // One summary per (tenant, employee, year_month) — partial unique (soft-delete aware).
        builder.HasIndex(s => new { s.TenantId, s.EmployeeId, s.YearMonth })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_attendance_monthly_summary_unique");

        // Month-scoped list read for the HR summary page / export.
        builder.HasIndex(s => new { s.TenantId, s.YearMonth })
            .HasDatabaseName("ix_attendance_monthly_summary_tenant_month");
    }
}
