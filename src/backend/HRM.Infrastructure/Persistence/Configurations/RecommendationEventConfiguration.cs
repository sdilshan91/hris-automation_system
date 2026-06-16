using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="RecommendationEvent"/> (US-PRF-010 FR-4/FR-7). Maps to the
/// "recommendation_event" table (singular snake_case). An IMMUTABLE, append-only audit log — no concurrency token
/// and no update path (mirrors PipEvent). Tenant isolation via the global query filter + TenantInterceptor.
/// </summary>
public sealed class RecommendationEventConfiguration : IEntityTypeConfiguration<RecommendationEvent>
{
    public void Configure(EntityTypeBuilder<RecommendationEvent> builder)
    {
        builder.ToTable("recommendation_event");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.RecommendationId).IsRequired();

        builder.Property(e => e.EventType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.ActorUserId);
        builder.Property(e => e.ActorEmployeeId);
        builder.Property(e => e.ActorName).HasMaxLength(300).IsRequired();
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.ClientIpAddress).HasMaxLength(64);
        builder.Property(e => e.Detail).HasMaxLength(4000);

        builder.Property(e => e.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(e => new { e.RecommendationId, e.OccurredAt });
    }
}
