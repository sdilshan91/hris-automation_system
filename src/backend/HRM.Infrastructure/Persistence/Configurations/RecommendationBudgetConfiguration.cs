using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="RecommendationBudget"/> (US-PRF-010 FR-8). Maps to the
/// "recommendation_budget" table (singular snake_case). Tenant isolation via the global query filter +
/// TenantInterceptor. At most one budget per cycle per tenant (tenant-scoped partial unique index). The amount
/// columns are stored as plain numeric (NFR-3 encryption seam — see the RecommendationBudget xmldoc). Optimistic
/// concurrency rides the Npgsql xmin row-version convention so concurrent submissions don't lose consumed updates.
/// </summary>
public sealed class RecommendationBudgetConfiguration : IEntityTypeConfiguration<RecommendationBudget>
{
    public void Configure(EntityTypeBuilder<RecommendationBudget> builder)
    {
        builder.ToTable("recommendation_budget");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");

        builder.Property(b => b.CycleId).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();

        // SENSITIVE (NFR-3 encryption seam — stored plain numeric today; see RecommendationBudget xmldoc).
        builder.Property(b => b.AllocatedAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(b => b.ConsumedAmount).HasPrecision(18, 2).IsRequired();

        builder.Property(b => b.Currency).HasMaxLength(3).IsRequired();

        builder.Property(b => b.IsDeleted).HasDefaultValue(false).IsRequired();
        builder.Property(b => b.Version).IsRowVersion();

        builder.HasIndex(b => new { b.TenantId, b.CycleId })
            .IsUnique()
            .HasFilter("is_deleted = false");
    }
}
