using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="PayrollReportExport"/> entity (ISSUE-178 PR2). Maps to
/// "payroll_report_exports" (snake_case). Tenant isolation via the global query filter + TenantInterceptor.
/// Indexes support the two hot reads: the per-user concurrency count (by tenant + requested-by + status) and the
/// history list (by tenant + requested_at, newest-first). A deliberate 1:1 clone of
/// <see cref="HrReportExportConfiguration"/>.
/// </summary>
public sealed class PayrollReportExportConfiguration : IEntityTypeConfiguration<PayrollReportExport>
{
    public void Configure(EntityTypeBuilder<PayrollReportExport> builder)
    {
        builder.ToTable("payroll_report_exports");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.RequestedByUserId).IsRequired();

        builder.Property(e => e.ReportType).HasMaxLength(50).IsRequired();

        builder.Property(e => e.Format)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // The resolved report filters — can be moderately large; stored as jsonb on PostgreSQL.
        builder.Property(e => e.FiltersJson).HasColumnType("jsonb").IsRequired();

        builder.Property(e => e.RowCount).IsRequired();
        builder.Property(e => e.FileSizeBytes).IsRequired();
        builder.Property(e => e.FilePath).HasMaxLength(1000);
        builder.Property(e => e.Error).HasMaxLength(2000);
        builder.Property(e => e.RequestedAt).IsRequired();
        builder.Property(e => e.IsDeleted).HasDefaultValue(false).IsRequired();

        // Concurrency check: "how many Queued/Processing exports does this user have?".
        builder.HasIndex(e => new { e.TenantId, e.RequestedByUserId, e.Status })
            .HasDatabaseName("ix_payroll_report_exports_tenant_user_status");

        // History list: newest-first per tenant.
        builder.HasIndex(e => new { e.TenantId, e.RequestedAt })
            .HasDatabaseName("ix_payroll_report_exports_tenant_requested_at");
    }
}
