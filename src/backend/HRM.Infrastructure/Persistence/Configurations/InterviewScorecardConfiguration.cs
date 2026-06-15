using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the InterviewScorecard entity (US-REC-006). Maps to the "interview_scorecard"
/// table with snake_case naming. Tenant isolation is via the global query filter in AppDbContext +
/// TenantInterceptor (this codebase does not use Postgres RLS — same as every other module). The
/// recommendation enum is stored as a string (matching the codebase's varchar status/type pattern). One
/// scorecard per interviewer per interview is enforced by the unique index (FR-2).
/// </summary>
public sealed class InterviewScorecardConfiguration : IEntityTypeConfiguration<InterviewScorecard>
{
    public void Configure(EntityTypeBuilder<InterviewScorecard> builder)
    {
        builder.ToTable("interview_scorecard");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.InterviewId).IsRequired();
        builder.Property(s => s.InterviewerEmployeeId).IsRequired();

        builder.Property(s => s.OverallRecommendation)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.AverageScore)
            .HasColumnType("numeric(4,2)")
            .IsRequired();

        builder.Property(s => s.GeneralNotes).HasColumnType("text");

        builder.Property(s => s.SubmittedAt).IsRequired();
        builder.Property(s => s.LockedAt).IsRequired();

        builder.Property(s => s.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasMany(s => s.Ratings)
            .WithOne(r => r.Scorecard)
            .HasForeignKey(r => r.ScorecardId)
            .OnDelete(DeleteBehavior.Cascade);

        // Story-requested index: the per-interview read (scorecards-for-interview).
        builder.HasIndex(s => new { s.TenantId, s.InterviewId })
            .HasDatabaseName("ix_interview_scorecard_tenant_interview");

        // FR-2: exactly one scorecard per interviewer per interview. Partial — exclude soft-deleted.
        builder.HasIndex(s => new { s.TenantId, s.InterviewId, s.InterviewerEmployeeId })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_interview_scorecard_tenant_interview_interviewer");
    }
}
