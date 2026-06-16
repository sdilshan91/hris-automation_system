using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="RecommendationRule"/> (US-PRF-010 FR-2/BR-3). Maps to the
/// "recommendation_rule" table (singular snake_case). Tenant-configurable auto-generation rules (rules are config,
/// not hard-coded — §10). Tenant isolation via the global query filter + TenantInterceptor.
/// </summary>
public sealed class RecommendationRuleConfiguration : IEntityTypeConfiguration<RecommendationRule>
{
    public void Configure(EntityTypeBuilder<RecommendationRule> builder)
    {
        builder.ToTable("recommendation_rule");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.MinFinalScore).HasPrecision(9, 4).IsRequired();

        builder.Property(r => r.RecommendedType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.DefaultBonusPercent).HasPrecision(9, 4);
        builder.Property(r => r.DefaultIncrementPercent).HasPrecision(9, 4);

        builder.Property(r => r.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(r => r.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.IsActive });
    }
}
