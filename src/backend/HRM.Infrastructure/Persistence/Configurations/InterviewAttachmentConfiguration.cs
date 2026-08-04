using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="InterviewAttachment"/> (US-REC-005 FR-8). Maps to
/// "interview_attachment" with snake_case naming; tenant isolation is the global query filter +
/// TenantInterceptor, as everywhere else in this module.
/// </summary>
public sealed class InterviewAttachmentConfiguration : IEntityTypeConfiguration<InterviewAttachment>
{
    public void Configure(EntityTypeBuilder<InterviewAttachment> builder)
    {
        builder.ToTable("interview_attachment");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.InterviewId).IsRequired();

        builder.Property(a => a.FileName).HasMaxLength(260).IsRequired();
        builder.Property(a => a.StorageKey).HasMaxLength(1024).IsRequired();
        builder.Property(a => a.FileSizeBytes).IsRequired();
        builder.Property(a => a.MimeType).HasMaxLength(128).IsRequired();

        // Stored as a string so a value added later cannot silently shift the meaning of existing rows.
        builder.Property(a => a.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(a => a.Description).HasColumnType("text");
        builder.Property(a => a.UploadedBy).IsRequired();

        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();

        // The only read: "this interview's attachments".
        builder.HasIndex(a => new { a.TenantId, a.InterviewId })
            .HasDatabaseName("ix_interview_attachment_tenant_interview");
    }
}
