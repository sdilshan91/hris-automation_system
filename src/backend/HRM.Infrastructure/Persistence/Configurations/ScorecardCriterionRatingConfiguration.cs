using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the ScorecardCriterionRating entity (US-REC-006 FR-1). Maps to the
/// "scorecard_criterion_rating" table with snake_case naming. Tenant isolation is via the global query
/// filter in AppDbContext + TenantInterceptor (no Postgres RLS — same as every other module).
/// </summary>
public sealed class ScorecardCriterionRatingConfiguration : IEntityTypeConfiguration<ScorecardCriterionRating>
{
    public void Configure(EntityTypeBuilder<ScorecardCriterionRating> builder)
    {
        builder.ToTable("scorecard_criterion_rating");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.ScorecardId).IsRequired();

        builder.Property(r => r.CriterionKey)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.Score).IsRequired();
        builder.Property(r => r.Comment).HasColumnType("text");

        builder.Property(r => r.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.ScorecardId })
            .HasDatabaseName("ix_scorecard_criterion_rating_tenant_scorecard");
    }
}
