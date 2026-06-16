using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="RecommendationApprover"/> (US-PRF-010 FR-4). Maps to the
/// "recommendation_approver" table (singular snake_case). Tenant isolation via the global query filter +
/// TenantInterceptor. The approver employee FK is a UUID column without a hard FK constraint (cross-module
/// reference pattern). One row per step per recommendation.
/// </summary>
public sealed class RecommendationApproverConfiguration : IEntityTypeConfiguration<RecommendationApprover>
{
    public void Configure(EntityTypeBuilder<RecommendationApprover> builder)
    {
        builder.ToTable("recommendation_approver");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.RecommendationId).IsRequired();
        builder.Property(a => a.ApproverEmployeeId).IsRequired();
        builder.Property(a => a.StepOrder).IsRequired();

        builder.Property(a => a.Decision)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.DecidedAt);
        builder.Property(a => a.Comment).HasMaxLength(2000);

        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(a => new { a.RecommendationId, a.StepOrder });
    }
}
