using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the PayrollApprovalHistory entity (US-PAY-008 FR-7/NFR-5). Maps to the
/// "payroll_approval_history" table with snake_case naming. Tenant-scoped via the global query filter +
/// TenantInterceptor. Mirrors <see cref="RegularizationApprovalHistoryConfiguration"/> /
/// <see cref="LeaveApprovalHistoryConfiguration"/>.
/// </summary>
public sealed class PayrollApprovalHistoryConfiguration : IEntityTypeConfiguration<PayrollApprovalHistory>
{
    public void Configure(EntityTypeBuilder<PayrollApprovalHistory> builder)
    {
        builder.ToTable("payroll_approval_history");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnName("id");

        builder.Property(h => h.PayrollRunId).IsRequired();
        builder.Property(h => h.WorkflowInstanceId).IsRequired();
        builder.Property(h => h.StepNumber).HasDefaultValue(1).IsRequired();

        builder.Property(h => h.Action).HasMaxLength(20).IsRequired();
        builder.Property(h => h.ActorUserId).IsRequired();
        builder.Property(h => h.Comments).HasColumnType("text");
        builder.Property(h => h.ActedAt).IsRequired();
        builder.Property(h => h.IpAddress).HasMaxLength(50);

        builder.Property(h => h.IsDeleted).HasDefaultValue(false).IsRequired();

        // ── Relationships ──────────────────────────────────────────
        builder.HasOne(h => h.PayrollRun)
            .WithMany()
            .HasForeignKey(h => h.PayrollRunId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Indexes ─────────────────────────────────────────────────
        // Primary lookup: a run's approval timeline, tenant-scoped, ordered by ActedAt.
        builder.HasIndex(h => new { h.TenantId, h.PayrollRunId })
            .HasDatabaseName("ix_payroll_approval_history_tenant_run");
    }
}
