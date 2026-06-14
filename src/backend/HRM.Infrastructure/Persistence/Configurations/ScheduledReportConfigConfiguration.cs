using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the ScheduledReportConfig entity (US-ATT-010 §7). Maps to the
/// "scheduled_report_config" table with snake_case naming. filters → jsonb, recipients → uuid[],
/// delivery_time → time. Tenant-scoped via the global query filter in AppDbContext.
/// </summary>
public sealed class ScheduledReportConfigConfiguration : IEntityTypeConfiguration<ScheduledReportConfig>
{
    public void Configure(EntityTypeBuilder<ScheduledReportConfig> builder)
    {
        builder.ToTable("scheduled_report_config");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id");

        builder.Property(c => c.ReportType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.Frequency)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(c => c.FiltersJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(c => c.Recipients)
            .HasColumnType("uuid[]")
            .IsRequired();

        builder.Property(c => c.DeliveryTime)
            .HasColumnType("time")
            .IsRequired();

        builder.Property(c => c.Format)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(c => c.IsActive)
            .IsRequired();

        builder.Property(c => c.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // Tenant-scoped list read for the scheduled-report management page + the due-config sweep.
        builder.HasIndex(c => new { c.TenantId, c.IsActive })
            .HasDatabaseName("ix_scheduled_report_config_tenant_active");
    }
}
