using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ReviewMeetingNotes"/> (US-PRF-006). Maps to the
/// "review_meeting_notes" table (singular snake_case). One per manager review. Tenant isolation is via the
/// global query filter in AppDbContext + TenantInterceptor. Optimistic concurrency rides the Npgsql xmin
/// row-version convention (mirrors ManagerReview). The rich-text fields hold SANITIZED HTML (the service
/// sanitizes on write) so column types are plain text.
/// </summary>
public sealed class ReviewMeetingNotesConfiguration : IEntityTypeConfiguration<ReviewMeetingNotes>
{
    public void Configure(EntityTypeBuilder<ReviewMeetingNotes> builder)
    {
        builder.ToTable("review_meeting_notes");

        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).HasColumnName("id");

        builder.Property(n => n.ManagerReviewId).IsRequired();

        // Sanitized HTML sections — unbounded text (a rich-text body can be long).
        builder.Property(n => n.Body).HasColumnType("text");
        builder.Property(n => n.Strengths).HasColumnType("text");
        builder.Property(n => n.DevelopmentAreas).HasColumnType("text");
        builder.Property(n => n.Summary).HasColumnType("text");

        builder.Property(n => n.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.Property(n => n.Version).IsRowVersion();

        // One notes row per manager review. Tenant-scoped, soft-delete-aware.
        builder.HasIndex(n => new { n.TenantId, n.ManagerReviewId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasMany(n => n.Actions)
            .WithOne(a => a.ReviewMeetingNotes!)
            .HasForeignKey(a => a.ReviewMeetingNotesId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
