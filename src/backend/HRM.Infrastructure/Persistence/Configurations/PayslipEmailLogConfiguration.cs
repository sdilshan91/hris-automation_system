using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the PayslipEmailLog entity (US-PAY-011 §7 / FR-5). Maps to the
/// "payslip_email_log" table with snake_case naming. Tenant-scoped via the global query filter +
/// TenantInterceptor. Mirrors <see cref="PayrollApprovalHistoryConfiguration"/>.
/// </summary>
public sealed class PayslipEmailLogConfiguration : IEntityTypeConfiguration<PayslipEmailLog>
{
    public void Configure(EntityTypeBuilder<PayslipEmailLog> builder)
    {
        builder.ToTable("payslip_email_log");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.PayrollRunId).IsRequired();
        builder.Property(x => x.PayrollSlipId).IsRequired();
        builder.Property(x => x.EmployeeId).IsRequired();

        builder.Property(x => x.RecipientEmail).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SentAt);
        builder.Property(x => x.FailureReason).HasColumnType("text");
        builder.Property(x => x.RetryCount).HasDefaultValue(0).IsRequired();

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // Primary lookup: a run's distribution rows, tenant-scoped (the summary + resume + selective re-send).
        builder.HasIndex(x => new { x.TenantId, x.PayrollRunId })
            .HasDatabaseName("ix_payslip_email_log_tenant_run");

        // Per-employee resume/idempotency lookup (NFR-3): "is this employee already Sent for this run?".
        builder.HasIndex(x => new { x.TenantId, x.PayrollRunId, x.EmployeeId })
            .HasDatabaseName("ix_payslip_email_log_tenant_run_employee");
    }
}
