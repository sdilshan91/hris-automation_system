using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="OffboardingTaskInstance"/> (US-ONB-005). Maps to the
/// "offboarding_task_instance" table (snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor. The clearance_category, responsible_role, status, and clearance_status
/// enums are stored as strings; responsible_user_id, source_template_task_id, and linked_asset_id are
/// cross-module refs (no hard FK). Soft-deletable.
/// </summary>
public sealed class OffboardingTaskInstanceConfiguration : IEntityTypeConfiguration<OffboardingTaskInstance>
{
    public void Configure(EntityTypeBuilder<OffboardingTaskInstance> builder)
    {
        builder.ToTable("offboarding_task_instance");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.OffboardingInstanceId).IsRequired();
        builder.Property(x => x.SourceTemplateTaskId);

        builder.Property(x => x.ClearanceCategory)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Description).HasMaxLength(1000);

        builder.Property(x => x.ResponsibleRole)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.ResponsibleUserId);

        builder.Property(x => x.DueDate).IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.IsMandatory).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.SortOrder).IsRequired();

        // AC-3 clearance decision (nullable enum stored as string).
        builder.Property(x => x.ClearanceStatus)
            .HasConversion<string?>()
            .HasMaxLength(20);

        builder.Property(x => x.Remarks).HasMaxLength(1000);
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.CompletedByUserId);

        // AC-2 / BR-3 asset return (cross-module ref, no hard FK).
        builder.Property(x => x.LinkedAssetId);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // Ordered read of an offboarding's tasks (by instance, then sort order).
        builder.HasIndex(x => new { x.TenantId, x.OffboardingInstanceId, x.SortOrder });
    }
}
