using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="OnboardingTaskInstance"/> (US-ONB-002). Maps to the
/// "onboarding_task_instance" table (snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor. The responsible_role and status enums are stored as strings;
/// responsible_user_id and source_template_task_id are cross-module refs (no hard FK), validated/resolved
/// in the service. Soft-deletable (AC-4 removed tasks); mandatory tasks cannot be removed (BR-3).
/// </summary>
public sealed class OnboardingTaskInstanceConfiguration : IEntityTypeConfiguration<OnboardingTaskInstance>
{
    public void Configure(EntityTypeBuilder<OnboardingTaskInstance> builder)
    {
        builder.ToTable("onboarding_task_instance");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ChecklistInstanceId).IsRequired();
        builder.Property(x => x.SourceTemplateTaskId);

        builder.Property(x => x.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Category).HasMaxLength(100);

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

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // Ordered read of a checklist's tasks (by instance, then sort order).
        builder.HasIndex(x => new { x.TenantId, x.ChecklistInstanceId, x.SortOrder });
    }
}
