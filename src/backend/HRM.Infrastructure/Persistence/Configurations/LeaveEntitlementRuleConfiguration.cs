using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the LeaveEntitlementRule entity (US-LV-002).
/// </summary>
public sealed class LeaveEntitlementRuleConfiguration : IEntityTypeConfiguration<LeaveEntitlementRule>
{
    public void Configure(EntityTypeBuilder<LeaveEntitlementRule> builder)
    {
        builder.ToTable("leave_entitlement_rules");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id");

        builder.Property(r => r.LeaveTypeId)
            .IsRequired();

        builder.Property(r => r.DepartmentId);

        builder.Property(r => r.JobTitleId);

        // job_level_id: nullable UUID WITHOUT hard FK (no JobLevel entity exists).
        // snake_case convention auto-names the column.
        builder.Property(r => r.JobLevelId);

        builder.Property(r => r.EmploymentType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(r => r.TenureMinMonths);

        builder.Property(r => r.TenureMaxMonths);

        builder.Property(r => r.EntitlementDays)
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        builder.Property(r => r.Priority)
            .IsRequired();

        builder.Property(r => r.EffectiveFrom)
            .IsRequired();

        builder.Property(r => r.EffectiveTo);

        builder.Property(r => r.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(r => r.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Relationships ──────────────────────────────────────────

        builder.HasOne(r => r.LeaveType)
            .WithMany()
            .HasForeignKey(r => r.LeaveTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Department)
            .WithMany()
            .HasForeignKey(r => r.DepartmentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(r => r.JobTitle)
            .WithMany()
            .HasForeignKey(r => r.JobTitleId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Indexes ────────────────────────────────────────────────

        // Primary lookup: find rules for a specific leave type within a tenant.
        builder.HasIndex(r => new { r.TenantId, r.LeaveTypeId })
            .HasDatabaseName("ix_leave_entitlement_rules_tenant_id_leave_type_id");

        // Active rules lookup with priority ordering.
        builder.HasIndex(r => new { r.TenantId, r.IsActive, r.Priority })
            .HasDatabaseName("ix_leave_entitlement_rules_tenant_active_priority");
    }
}
