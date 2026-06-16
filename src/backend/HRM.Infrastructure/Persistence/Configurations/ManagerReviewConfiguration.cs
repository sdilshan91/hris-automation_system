using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="ManagerReview"/> entity (US-PRF-003). Maps to the
/// "manager_review" table (singular snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor (no Postgres RLS — same as every other module). Optimistic concurrency
/// rides the Npgsql xmin row-version convention (NFR-3).
/// </summary>
public sealed class ManagerReviewConfiguration : IEntityTypeConfiguration<ManagerReview>
{
    public void Configure(EntityTypeBuilder<ManagerReview> builder)
    {
        builder.ToTable("manager_review");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.CycleId).IsRequired();
        builder.Property(r => r.EmployeeId).IsRequired();
        builder.Property(r => r.ReviewerEmployeeId);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Flag)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // US-PRF-006: sign-off workflow state on the review.
        builder.Property(r => r.SignoffStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();
        builder.Property(r => r.SignoffRequestedAt);
        builder.Property(r => r.SignoffCompletedAt);
        builder.Property(r => r.IsLocked).HasDefaultValue(false).IsRequired();

        builder.Property(r => r.WeightedManagerScore).HasColumnType("numeric(6,2)");
        builder.Property(r => r.FinalScore).HasColumnType("numeric(6,2)");
        builder.Property(r => r.SelfScoreAtSubmit).HasColumnType("numeric(6,2)");

        // FR-5: overall summary comment, max 5000 chars.
        builder.Property(r => r.SummaryComment).HasMaxLength(5000);

        builder.Property(r => r.IsDeleted).HasDefaultValue(false).IsRequired();

        // NFR-3 optimistic concurrency: maps to the PostgreSQL xmin system column (no schema DDL). Mirrors
        // Goal.Version / SelfAssessment.Version. The InMemory test provider ignores this annotation.
        builder.Property(r => r.Version).IsRowVersion();

        // Exactly one manager review per employee per cycle (US-PRF-003 §7). Tenant-scoped, soft-delete-aware.
        builder.HasIndex(r => new { r.TenantId, r.CycleId, r.EmployeeId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasMany(r => r.Items)
            .WithOne(i => i.ManagerReview!)
            .HasForeignKey(i => i.ManagerReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        // US-PRF-006: one-to-one meeting notes; one-to-many immutable sign-off log.
        builder.HasOne(r => r.MeetingNotes)
            .WithOne(n => n.ManagerReview!)
            .HasForeignKey<ReviewMeetingNotes>(n => n.ManagerReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(r => r.Signoffs)
            .WithOne(s => s.ManagerReview!)
            .HasForeignKey(s => s.ManagerReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
