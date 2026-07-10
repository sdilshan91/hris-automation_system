using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the live <see cref="WorkflowInstance"/> (US-ADM-011 FR-1). Maps to
/// "workflow_instances" (snake_case). Tenant isolation via the global query filter + TenantInterceptor
/// (RLS-eligible). Step-instances are a child table with cascade delete.
/// </summary>
public sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("workflow_instances");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.WorkflowDefinitionId).IsRequired();
        builder.Property(i => i.LineageId).IsRequired();
        builder.Property(i => i.Version).IsRequired();

        builder.Property(i => i.EntityType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(i => i.EntityId).IsRequired();

        builder.Property(i => i.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(WorkflowInstanceStatus.InProgress)
            .IsRequired();

        builder.Property(i => i.CurrentStepOrder).HasDefaultValue(0).IsRequired();
        builder.Property(i => i.RouteStepOrders).HasMaxLength(400).HasDefaultValue(string.Empty).IsRequired();
        builder.Property(i => i.RequesterUserId);
        builder.Property(i => i.RequesterEmployeeId);
        builder.Property(i => i.CompletedAt);

        builder.Property(i => i.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasMany(i => i.StepInstances)
            .WithOne()
            .HasForeignKey(s => s.WorkflowInstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        // AC-8/FR-10: the in-flight count query is (tenant, lineage, status). One instance per entity request.
        builder.HasIndex(i => new { i.TenantId, i.LineageId, i.Status })
            .HasDatabaseName("ix_workflow_instances_tenant_lineage_status");

        // Decide() looks up the in-flight instance by (tenant, entity_type, entity_id).
        builder.HasIndex(i => new { i.TenantId, i.EntityType, i.EntityId })
            .HasDatabaseName("ix_workflow_instances_tenant_entity");
    }
}
