using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="GoalProgressAttachment"/> (US-PRF-009 FR-2). Maps to the
/// "goal_progress_attachment" table (singular snake_case). Metadata-only — the blob lives in tenant-scoped file
/// storage (the US-PRF-008 PipCheckpoint attachment-seam approach). Tenant isolation is via the global query filter
/// in AppDbContext + TenantInterceptor (NFR-2).
/// </summary>
public sealed class GoalProgressAttachmentConfiguration : IEntityTypeConfiguration<GoalProgressAttachment>
{
    public void Configure(EntityTypeBuilder<GoalProgressAttachment> builder)
    {
        builder.ToTable("goal_progress_attachment");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.ProgressUpdateId).IsRequired();
        builder.Property(a => a.FileName).HasMaxLength(512).IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(1024).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(255);
        builder.Property(a => a.SizeBytes);

        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.ProgressUpdateId });
    }
}
