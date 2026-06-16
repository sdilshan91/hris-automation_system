using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ReviewMeetingNotesAction"/> (US-PRF-006 FR-2). Maps to the
/// "review_meeting_notes_action" table (singular snake_case). Tenant isolation is via the global query filter
/// in AppDbContext + TenantInterceptor.
/// </summary>
public sealed class ReviewMeetingNotesActionConfiguration : IEntityTypeConfiguration<ReviewMeetingNotesAction>
{
    public void Configure(EntityTypeBuilder<ReviewMeetingNotesAction> builder)
    {
        builder.ToTable("review_meeting_notes_action");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.ReviewMeetingNotesId).IsRequired();

        builder.Property(a => a.Description).HasMaxLength(1000).IsRequired();
        builder.Property(a => a.Deadline);
        builder.Property(a => a.SortOrder).HasDefaultValue(0).IsRequired();

        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.ReviewMeetingNotesId });
    }
}
