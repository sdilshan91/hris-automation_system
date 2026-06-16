using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="SubscriptionPlan"/> (US-ADM-001). Platform/system table
/// "subscription_plans" — NOT tenant-scoped (no global query filter applied in AppDbContext).
/// </summary>
public sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("subscription_plans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.Property(p => p.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.Code)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(p => p.Code)
            .IsUnique();

        builder.Property(p => p.PriceMonthly)
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(p => p.TrialDays)
            .IsRequired();

        builder.Property(p => p.MaxEmployees);

        builder.Property(p => p.IsActive)
            .HasDefaultValue(true)
            .IsRequired();
    }
}
