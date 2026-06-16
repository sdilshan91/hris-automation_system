using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="SelfAssessmentAttachment"/> entity (US-PRF-002 FR-5/NFR-4).
/// Maps to the "self_assessment_attachment" table (singular snake_case). Tenant isolation is via the
/// global query filter in AppDbContext + TenantInterceptor.
/// </summary>
public sealed class SelfAssessmentAttachmentConfiguration : IEntityTypeConfiguration<SelfAssessmentAttachment>
{
    public void Configure(EntityTypeBuilder<SelfAssessmentAttachment> builder)
    {
        builder.ToTable("self_assessment_attachment");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.SelfAssessmentItemId).IsRequired();

        builder.Property(a => a.FileName)
            .HasMaxLength(260)
            .IsRequired();

        builder.Property(a => a.ContentType)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(a => a.SizeBytes).IsRequired();

        builder.Property(a => a.StorageKey)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(a => a.IsScanned).HasDefaultValue(false).IsRequired();

        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.SelfAssessmentItemId });
    }
}
