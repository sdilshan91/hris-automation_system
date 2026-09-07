using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="LeaveRequestAttachment"/> entity (US-LV-003 FR-5 / ISSUE-036).
/// Maps to the "leave_request_attachment" table (singular snake_case). Tenant isolation is via the global
/// query filter in AppDbContext + TenantInterceptor. Mirrors SelfAssessmentAttachmentConfiguration.
/// </summary>
public sealed class LeaveRequestAttachmentConfiguration : IEntityTypeConfiguration<LeaveRequestAttachment>
{
    public void Configure(EntityTypeBuilder<LeaveRequestAttachment> builder)
    {
        builder.ToTable("leave_request_attachment");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        // Nullable: the upload precedes the leave request and is linked on create (ISSUE-036).
        builder.Property(a => a.LeaveRequestId);

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

        builder.Property(a => a.UploadedByEmployeeId).IsRequired();

        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(a => new { a.TenantId, a.LeaveRequestId });
    }
}
