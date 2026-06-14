using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the LeaveCarryForwardTracking entity (US-LV-008 §7).
/// Maps to "leave_carry_forward_tracking" with snake_case naming.
/// </summary>
public sealed class LeaveCarryForwardTrackingConfiguration
    : IEntityTypeConfiguration<LeaveCarryForwardTracking>
{
    public void Configure(EntityTypeBuilder<LeaveCarryForwardTracking> builder)
    {
        builder.ToTable("leave_carry_forward_tracking");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id");

        builder.Property(t => t.EmployeeId)
            .IsRequired();

        builder.Property(t => t.LeaveTypeId)
            .IsRequired();

        builder.Property(t => t.FromYear)
            .IsRequired();

        builder.Property(t => t.ToYear)
            .IsRequired();

        builder.Property(t => t.CarriedDays)
            .HasColumnType("numeric(7,2)")
            .IsRequired();

        builder.Property(t => t.ExpiryDate)
            .HasColumnType("date");

        builder.Property(t => t.ExpiredDays)
            .HasColumnType("numeric(7,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(t => t.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // FK to Employee (CASCADE — mirrors LeaveLedger / overrides).
        builder.HasOne(t => t.Employee)
            .WithMany()
            .HasForeignKey(t => t.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK to LeaveType (RESTRICT — mirrors LeaveLedger / rules; keeps historical tracking).
        builder.HasOne(t => t.LeaveType)
            .WithMany()
            .HasForeignKey(t => t.LeaveTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // NFR-3 idempotency: at most one carry-forward row per employee/type/from→to year.
        builder.HasIndex(t => new { t.TenantId, t.EmployeeId, t.LeaveTypeId, t.FromYear, t.ToYear })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_leave_carry_forward_tracking_unique");

        // FR-3 expiry scan: locate Active rows whose expiry date has passed.
        builder.HasIndex(t => new { t.TenantId, t.Status, t.ExpiryDate })
            .HasDatabaseName("ix_leave_carry_forward_tracking_status_expiry");
    }
}
