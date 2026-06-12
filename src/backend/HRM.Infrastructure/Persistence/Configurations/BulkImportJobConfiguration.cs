using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for BulkImportJob (US-CHR-010).
/// </summary>
public sealed class BulkImportJobConfiguration : IEntityTypeConfiguration<BulkImportJob>
{
    public void Configure(EntityTypeBuilder<BulkImportJob> builder)
    {
        builder.ToTable("bulk_import_jobs");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");

        builder.Property(b => b.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(b => b.TotalRows);
        builder.Property(b => b.SuccessCount);
        builder.Property(b => b.FailedCount);
        builder.Property(b => b.ProcessedRows);

        builder.Property(b => b.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(b => b.ErrorDetails)
            .HasColumnType("jsonb");

        builder.Property(b => b.InitiatedBy)
            .HasMaxLength(256);

        builder.Property(b => b.HangfireJobId)
            .HasMaxLength(256);

        // Index for querying by tenant + status (most common query pattern)
        builder.HasIndex(b => new { b.TenantId, b.Status })
            .HasDatabaseName("ix_bulk_import_jobs_tenant_id_status");
    }
}
