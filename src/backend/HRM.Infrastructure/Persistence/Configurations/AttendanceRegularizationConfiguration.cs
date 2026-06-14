using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the AttendanceRegularization entity (US-ATT-003 §7).
/// Maps to the "attendance_regularization" table with snake_case naming convention.
/// </summary>
public sealed class AttendanceRegularizationConfiguration : IEntityTypeConfiguration<AttendanceRegularization>
{
    public void Configure(EntityTypeBuilder<AttendanceRegularization> builder)
    {
        builder.ToTable("attendance_regularization");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id");

        builder.Property(r => r.EmployeeId)
            .IsRequired();

        builder.Property(r => r.AttendanceLogId);

        builder.Property(r => r.Date)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(r => r.RegularizationType)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.RequestedClockIn)
            .HasColumnType("timestamptz");

        builder.Property(r => r.RequestedClockOut)
            .HasColumnType("timestamptz");

        builder.Property(r => r.Reason)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(r => r.Status)
            .HasMaxLength(20)
            .IsRequired();

        // FK to the workflow engine instance — no engine exists yet (US-ADM-007), stored nullable
        // with no FK constraint. TODO(US-ADM-007): add the relationship when the engine lands.
        builder.Property(r => r.WorkflowInstanceId);

        builder.Property(r => r.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Relationships ──────────────────────────────────────────

        builder.HasOne(r => r.Employee)
            .WithMany()
            .HasForeignKey(r => r.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Nullable FK to an existing log for the date (AC-2/BR-5). Restrict so the log can't be
        // cascade-deleted out from under a regularization.
        builder.HasOne(r => r.AttendanceLog)
            .WithMany()
            .HasForeignKey(r => r.AttendanceLogId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes (§7) ───────────────────────────────────────────

        // Lookup for an employee's regularizations on a date (duplicate-pending detection, AC-4/BR-3).
        builder.HasIndex(r => new { r.TenantId, r.EmployeeId, r.Date })
            .HasDatabaseName("ix_attendance_regularization_tenant_emp_date");

        // BR-3 / AC-4: at most one PENDING regularization per employee per date. Partial unique
        // index over pending, non-deleted records.
        builder.HasIndex(r => new { r.TenantId, r.EmployeeId, r.Date })
            .IsUnique()
            .HasFilter("status = 'PENDING' AND is_deleted = false")
            .HasDatabaseName("ix_attendance_regularization_pending_unique");
    }
}
