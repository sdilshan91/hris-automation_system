using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the LeaveEntitlementOverride entity (US-LV-002 AC-3).
/// </summary>
public sealed class LeaveEntitlementOverrideConfiguration : IEntityTypeConfiguration<LeaveEntitlementOverride>
{
    public void Configure(EntityTypeBuilder<LeaveEntitlementOverride> builder)
    {
        builder.ToTable("leave_entitlement_overrides");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id");

        builder.Property(o => o.EmployeeId)
            .IsRequired();

        builder.Property(o => o.LeaveTypeId)
            .IsRequired();

        builder.Property(o => o.LeaveYear)
            .IsRequired();

        builder.Property(o => o.EntitlementDays)
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        builder.Property(o => o.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(o => o.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Relationships ──────────────────────────────────────────

        builder.HasOne(o => o.Employee)
            .WithMany()
            .HasForeignKey(o => o.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.LeaveType)
            .WithMany()
            .HasForeignKey(o => o.LeaveTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ────────────────────────────────────────────────

        // Primary lookup: find overrides for an employee + leave type + year.
        builder.HasIndex(o => new { o.TenantId, o.EmployeeId, o.LeaveTypeId, o.LeaveYear })
            .HasDatabaseName("ix_leave_entitlement_overrides_tenant_emp_type_year");

        // Unique constraint: one override per employee per leave type per year.
        builder.HasIndex(o => new { o.TenantId, o.EmployeeId, o.LeaveTypeId, o.LeaveYear })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_leave_entitlement_overrides_unique");
    }
}
