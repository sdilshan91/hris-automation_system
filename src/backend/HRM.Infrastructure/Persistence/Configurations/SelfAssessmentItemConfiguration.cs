using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="SelfAssessmentItem"/> entity (US-PRF-002). Maps to the
/// "self_assessment_item" table (singular snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor.
/// </summary>
public sealed class SelfAssessmentItemConfiguration : IEntityTypeConfiguration<SelfAssessmentItem>
{
    public void Configure(EntityTypeBuilder<SelfAssessmentItem> builder)
    {
        builder.ToTable("self_assessment_item");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.SelfAssessmentId).IsRequired();
        builder.Property(i => i.GoalId).IsRequired();

        builder.Property(i => i.SelfRating);
        builder.Property(i => i.AchievementPercentage);

        builder.Property(i => i.Comment)
            .HasMaxLength(4000);

        builder.Property(i => i.IsDeleted).HasDefaultValue(false).IsRequired();

        // One item per goal within a self-assessment. Tenant-scoped, soft-delete-aware.
        builder.HasIndex(i => new { i.TenantId, i.SelfAssessmentId, i.GoalId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasMany(i => i.Attachments)
            .WithOne(a => a.SelfAssessmentItem!)
            .HasForeignKey(a => a.SelfAssessmentItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
