using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="WorkflowStepApprover"/> child entity (US-ADM-011b). Maps to
/// "workflow_step_approvers" (snake_case). Tenant isolation via the global query filter + TenantInterceptor
/// (RLS-eligible). The FK relationship to <see cref="WorkflowStep"/> is declared on the WorkflowStep side
/// (via its Approvers navigation) so the cascade is owned there.
/// </summary>
public sealed class WorkflowStepApproverConfiguration : IEntityTypeConfiguration<WorkflowStepApprover>
{
    public void Configure(EntityTypeBuilder<WorkflowStepApprover> builder)
    {
        builder.ToTable("workflow_step_approvers");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.WorkflowStepId).IsRequired();

        builder.Property(a => a.ApproverType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.ApproverIdentifier);

        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(a => a.WorkflowStepId)
            .HasDatabaseName("ix_workflow_step_approvers_workflow_step_id");
    }
}
