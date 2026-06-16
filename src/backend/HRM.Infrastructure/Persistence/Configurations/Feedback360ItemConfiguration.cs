using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="Feedback360Item"/> entity (US-PRF-005). Maps to the
/// "feedback_360_item" table (singular snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor. Each row rates one competency/goal on the cycle's scale.
/// </summary>
public sealed class Feedback360ItemConfiguration : IEntityTypeConfiguration<Feedback360Item>
{
    public void Configure(EntityTypeBuilder<Feedback360Item> builder)
    {
        builder.ToTable("feedback_360_item");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.Feedback360Id).IsRequired();

        builder.Property(i => i.GoalId);
        builder.Property(i => i.CompetencyKey).HasMaxLength(200);

        builder.Property(i => i.Rating).IsRequired();

        builder.Property(i => i.Comment).HasMaxLength(2000);

        builder.Property(i => i.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(i => new { i.TenantId, i.Feedback360Id });
    }
}
