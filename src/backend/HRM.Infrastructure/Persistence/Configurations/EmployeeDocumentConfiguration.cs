using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the EmployeeDocument entity (US-CHR-008).
/// Maps to the "employee_documents" table with snake_case naming convention.
/// </summary>
public sealed class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
    {
        builder.ToTable("employee_documents");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id");

        builder.Property(d => d.FileName)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(d => d.StorageKey)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(d => d.FileSizeBytes)
            .IsRequired();

        builder.Property(d => d.MimeType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.Category)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(d => d.Description)
            .HasColumnType("text");

        builder.Property(d => d.ExpiryDate)
            .HasColumnType("date");

        builder.Property(d => d.UploadedBy)
            .IsRequired();

        builder.Property(d => d.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Foreign keys ────────────────────────────────────────────

        builder.HasOne(d => d.Employee)
            .WithMany(e => e.Documents)
            .HasForeignKey(d => d.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Indexes ─────────────────────────────────────────────────

        builder.HasIndex(d => new { d.TenantId, d.EmployeeId })
            .HasDatabaseName("ix_employee_documents_tenant_id_employee_id");

        builder.HasIndex(d => new { d.TenantId, d.ExpiryDate })
            .HasDatabaseName("ix_employee_documents_tenant_id_expiry_date");

        builder.HasIndex(d => new { d.TenantId, d.Category })
            .HasDatabaseName("ix_employee_documents_tenant_id_category");
    }
}
