using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the LeaveRequest entity (US-LV-003 §7).
/// Maps to the "leave_request" table with snake_case naming convention.
/// </summary>
public sealed class LeaveRequestConfiguration : IEntityTypeConfiguration<LeaveRequest>
{
    public void Configure(EntityTypeBuilder<LeaveRequest> builder)
    {
        builder.ToTable("leave_request");

        builder.HasKey(lr => lr.Id);

        builder.Property(lr => lr.Id)
            .HasColumnName("id");

        builder.Property(lr => lr.EmployeeId)
            .IsRequired();

        builder.Property(lr => lr.LeaveTypeId)
            .IsRequired();

        builder.Property(lr => lr.StartDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(lr => lr.EndDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(lr => lr.IsHalfDay)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(lr => lr.HalfDaySession)
            .HasMaxLength(2);

        builder.Property(lr => lr.TotalDays)
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        builder.Property(lr => lr.Reason)
            .HasColumnType("text");

        builder.Property(lr => lr.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(lr => lr.RequestedAt)
            .IsRequired();

        // PostgreSQL text[] for attachment URLs (§7).
        builder.Property(lr => lr.AttachmentUrls)
            .HasColumnType("text[]");

        builder.Property(lr => lr.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Relationships ──────────────────────────────────────────

        builder.HasOne(lr => lr.Employee)
            .WithMany()
            .HasForeignKey(lr => lr.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(lr => lr.LeaveType)
            .WithMany()
            .HasForeignKey(lr => lr.LeaveTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes (§7) ───────────────────────────────────────────

        // Composite index for an employee's requests filtered by status and date.
        builder.HasIndex(lr => new { lr.TenantId, lr.EmployeeId, lr.Status, lr.StartDate })
            .HasDatabaseName("ix_leave_request_tenant_emp_status_start");

        // Partial index for pending-request lookups (overlap detection / approval queues).
        builder.HasIndex(lr => new { lr.TenantId, lr.StartDate })
            .HasFilter("status = 'Pending'")
            .HasDatabaseName("ix_leave_pending");
    }
}
