using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="PipCheckpoint"/> (US-PRF-008 AC-3/FR-4/FR-5). Maps to the
/// "pip_checkpoint" table (singular snake_case), an append-only history — immutability is enforced at the
/// service layer (no update/delete path). Tenant isolation is via the global query filter in AppDbContext +
/// TenantInterceptor.
/// </summary>
public sealed class PipCheckpointConfiguration : IEntityTypeConfiguration<PipCheckpoint>
{
    public void Configure(EntityTypeBuilder<PipCheckpoint> builder)
    {
        builder.ToTable("pip_checkpoint");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.PipId).IsRequired();
        builder.Property(c => c.CheckpointDate).IsRequired();

        builder.Property(c => c.ProgressStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.EvidenceNotes).HasMaxLength(4000).IsRequired();
        builder.Property(c => c.ReviewerEmployeeId);
        builder.Property(c => c.ReviewerName).HasMaxLength(300).IsRequired();
        builder.Property(c => c.RecordedAt).IsRequired();

        builder.Property(c => c.AttachmentStorageKey).HasMaxLength(1024);
        builder.Property(c => c.AttachmentFileName).HasMaxLength(512);
        builder.Property(c => c.AttachmentContentType).HasMaxLength(255);
        builder.Property(c => c.AttachmentSizeBytes);

        builder.Property(c => c.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.PipId, c.RecordedAt });
    }
}
