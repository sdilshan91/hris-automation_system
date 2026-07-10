using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the WorkflowStep child entity (US-ADM-007 FR-2). Maps to "workflow_steps"
/// (snake_case). Tenant isolation via the global query filter + TenantInterceptor. ConditionJson is stored
/// as jsonb on PostgreSQL.
/// </summary>
public sealed class WorkflowStepConfiguration : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> builder)
    {
        builder.ToTable("workflow_steps");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.WorkflowDefinitionId).IsRequired();
        builder.Property(s => s.StepOrder).IsRequired();

        builder.Property(s => s.ApproverType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.ApproverIdentifier);

        builder.Property(s => s.IsParallel).HasDefaultValue(false).IsRequired();
        builder.Property(s => s.SlaHours).IsRequired();

        builder.Property(s => s.EscalationApproverType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(s => s.EscalationApproverIdentifier);

        builder.Property(s => s.ConditionJson).HasColumnType("jsonb");

        builder.Property(s => s.DelegationEnabled).HasDefaultValue(false).IsRequired();
        builder.Property(s => s.DelegationBackupUserId);

        builder.Property(s => s.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(s => s.WorkflowDefinitionId)
            .HasDatabaseName("ix_workflow_steps_workflow_definition_id");

        builder.HasIndex(s => new { s.WorkflowDefinitionId, s.StepOrder })
            .HasDatabaseName("ix_workflow_steps_definition_step_order");

        // US-ADM-011b: additional parallel approvers are a cascade child collection of the step.
        builder.HasMany(s => s.Approvers)
            .WithOne()
            .HasForeignKey(a => a.WorkflowStepId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
