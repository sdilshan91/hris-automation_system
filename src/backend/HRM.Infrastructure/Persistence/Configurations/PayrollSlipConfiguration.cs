using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the PayrollSlip entity (US-PAY-003 FR-5). Maps to the "payroll_slip" table with
/// snake_case naming. Tenant isolation via the global query filter + TenantInterceptor. The composite index
/// on (tenant_id, payroll_run_id, employee_id) per §19.12 backs the "slips for a run" read and the re-run
/// replacement (FR-7).
/// </summary>
public sealed class PayrollSlipConfiguration : IEntityTypeConfiguration<PayrollSlip>
{
    public void Configure(EntityTypeBuilder<PayrollSlip> builder)
    {
        builder.ToTable("payroll_slip");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.PayrollRunId).IsRequired();
        builder.Property(x => x.EmployeeId).IsRequired();

        builder.Property(x => x.GrossEarnings).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.TotalDeductions).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.NetSalary).HasColumnType("numeric(18,2)").IsRequired();

        builder.Property(x => x.LopDays).HasColumnType("numeric(5,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.WorkingDays).HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(x => x.PaidDays).HasColumnType("numeric(5,2)").IsRequired();

        builder.Property(x => x.PayMonth).IsRequired();
        builder.Property(x => x.PayYear).IsRequired();

        // US-PAY-004 (§7): PDF generation tracking columns. All nullable — a slip exists before its PDF.
        builder.Property(x => x.PdfGeneratedAt);
        builder.Property(x => x.PdfStoragePath).HasMaxLength(500);
        builder.Property(x => x.PdfStatus).HasMaxLength(20);
        builder.Property(x => x.PdfFileSizeBytes);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // Owned details are managed via the dedicated DbSet, not as a mapped nav.
        builder.Ignore(x => x.Details);

        // §19.12 composite index: slips-for-a-run lookup + the FR-7 re-run replacement.
        builder.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.EmployeeId })
            .HasDatabaseName("ix_payroll_slip_tenant_run_employee");
    }
}
