using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="InterviewScorecardRevision"/> (US-REC-006 AC-K1). Maps to
/// "interview_scorecard_revision" with snake_case naming; tenant isolation is the global query filter +
/// TenantInterceptor, as everywhere else in this module.
/// </summary>
public sealed class InterviewScorecardRevisionConfiguration : IEntityTypeConfiguration<InterviewScorecardRevision>
{
    public void Configure(EntityTypeBuilder<InterviewScorecardRevision> builder)
    {
        builder.ToTable("interview_scorecard_revision");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.ScorecardId).IsRequired();
        builder.Property(r => r.Version).IsRequired();

        // Stored as a string like the other recruitment enums, so a value added later cannot silently shift
        // the meaning of rows already written.
        builder.Property(r => r.OverallRecommendation)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(r => r.AverageScore)
            .HasColumnType("numeric(4,2)")
            .IsRequired();

        builder.Property(r => r.GeneralNotes).HasColumnType("text");

        builder.Property(r => r.RatingsJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(r => r.RevisedAt).IsRequired();
        builder.Property(r => r.RevisedByEmployeeId).IsRequired();

        builder.Property(r => r.IsDeleted).HasDefaultValue(false).IsRequired();

        // The only read this feature performs: "give me this scorecard's history, oldest first".
        builder.HasIndex(r => new { r.TenantId, r.ScorecardId, r.Version })
            .HasDatabaseName("ix_interview_scorecard_revision_tenant_scorecard_version");
    }
}
