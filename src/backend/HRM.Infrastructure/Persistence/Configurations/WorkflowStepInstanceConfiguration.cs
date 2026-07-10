using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the live <see cref="WorkflowStepInstance"/> (US-ADM-011 FR-2). Maps to
/// "workflow_step_instances" (snake_case). Tenant isolation via the global query filter + TenantInterceptor
/// (RLS-eligible).
/// </summary>
public sealed class WorkflowStepInstanceConfiguration : IEntityTypeConfiguration<WorkflowStepInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowStepInstance> builder)
    {
        builder.ToTable("workflow_step_instances");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.WorkflowInstanceId).IsRequired();
        builder.Property(s => s.StepOrder).IsRequired();
        builder.Property(s => s.IsParallel).HasDefaultValue(false).IsRequired();

        builder.Property(s => s.ApproverType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.ApproverIdentifier);
        builder.Property(s => s.AssignedApproverUserId);

        builder.Property(s => s.Decision)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(WorkflowStepDecision.Pending)
            .IsRequired();

        builder.Property(s => s.DecidedByUserId);
        builder.Property(s => s.DecidedAt);
        builder.Property(s => s.Comments).HasMaxLength(2000);
        builder.Property(s => s.SlaDueAt);
        builder.Property(s => s.EscalatedAt);

        builder.Property(s => s.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(s => s.WorkflowInstanceId)
            .HasDatabaseName("ix_workflow_step_instances_instance_id");

        // The active-step lookup + the SLA-timer scan (US-ADM-011b) both filter on decision.
        builder.HasIndex(s => new { s.WorkflowInstanceId, s.Decision })
            .HasDatabaseName("ix_workflow_step_instances_instance_decision");
    }
}
