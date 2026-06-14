using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the LatePolicy entity (US-ATT-008 §7).
/// Maps to the "late_policy" table; exactly one row per tenant (partial unique index).
/// </summary>
public sealed class LatePolicyConfiguration : IEntityTypeConfiguration<LatePolicy>
{
    public void Configure(EntityTypeBuilder<LatePolicy> builder)
    {
        builder.ToTable("late_policy");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.Property(p => p.ThresholdCount)
            .HasDefaultValue(3)
            .IsRequired();

        builder.Property(p => p.DeductionDays)
            .HasColumnType("numeric(3,1)")
            .HasDefaultValue(0.5m)
            .IsRequired();

        builder.Property(p => p.Period)
            .HasMaxLength(10)
            .HasDefaultValue("MONTHLY")
            .IsRequired();

        builder.Property(p => p.NotificationOnLate)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.ChronicThreshold)
            .HasDefaultValue(5)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // One policy row per tenant.
        builder.HasIndex(p => p.TenantId)
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_late_policy_tenant_unique");
    }
}
