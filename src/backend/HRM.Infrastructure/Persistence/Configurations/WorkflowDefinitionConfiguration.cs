using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the WorkflowDefinition entity (US-ADM-007). Maps to "workflow_definitions"
/// (snake_case). Tenant isolation via the global query filter + TenantInterceptor. Steps are a child table
/// (<see cref="WorkflowStep"/>) with a cascade delete.
/// </summary>
public sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("workflow_definitions");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id");

        builder.Property(w => w.Name).HasMaxLength(150).IsRequired();

        builder.Property(w => w.EntityType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(w => w.LineageId).IsRequired();
        builder.Property(w => w.Version).IsRequired();

        builder.Property(w => w.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(WorkflowStatus.Draft)
            .IsRequired();

        builder.Property(w => w.IsActive).HasDefaultValue(false).IsRequired();
        builder.Property(w => w.IsDefault).HasDefaultValue(false).IsRequired();
        builder.Property(w => w.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasMany(w => w.Steps)
            .WithOne()
            .HasForeignKey(s => s.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        // One version number per lineage (a logical workflow never has two rows at the same version).
        builder.HasIndex(w => new { w.TenantId, w.LineageId, w.Version })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_workflow_definitions_tenant_lineage_version");

        // BR-2: at most one ACTIVE workflow per entity type per tenant. Enforced primarily in the service
        // (auto-archive on create/restore); a partial unique index gives a DB-level backstop.
        builder.HasIndex(w => new { w.TenantId, w.EntityType, w.IsActive })
            .IsUnique()
            .HasFilter("is_active = true AND is_deleted = false")
            .HasDatabaseName("ix_workflow_definitions_tenant_entitytype_active");

        // List query (AC-1): group/order by entity type for the current tenant.
        builder.HasIndex(w => new { w.TenantId, w.EntityType })
            .HasDatabaseName("ix_workflow_definitions_tenant_entitytype");
    }
}
