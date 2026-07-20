using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="PayrollApprovalStepConfig"/> (US-PAY-008 AC-4/FR-2, BUG-076). Maps to
/// "payroll_approval_step_config" with snake_case naming. Tenant isolation is via the AppDbContext global query
/// filter + TenantInterceptor + the dormant Postgres RLS policy shipped in the migration.
/// </summary>
public sealed class PayrollApprovalStepConfigConfiguration : IEntityTypeConfiguration<PayrollApprovalStepConfig>
{
    public void Configure(EntityTypeBuilder<PayrollApprovalStepConfig> builder)
    {
        builder.ToTable("payroll_approval_step_config");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.StepNumber).IsRequired();
        builder.Property(c => c.RoleId).IsRequired();

        // US-PAY-008 FR-3 (ISSUE-173): per-step approval SLA + backup approver role. Both nullable/opt-in — no
        // SLA configured means the step never escalates.
        builder.Property(c => c.SlaHours);
        builder.Property(c => c.BackupRoleId);

        builder.Property(c => c.IsDeleted).HasDefaultValue(false).IsRequired();

        // One row per (tenant, step): the step→role binding is unique per step within a tenant.
        builder.HasIndex(c => new { c.TenantId, c.StepNumber })
            .IsUnique()
            .HasDatabaseName("ux_payroll_approval_step_config_tenant_step");
    }
}
