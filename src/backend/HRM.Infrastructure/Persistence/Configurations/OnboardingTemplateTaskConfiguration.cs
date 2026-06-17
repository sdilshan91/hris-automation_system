using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="OnboardingTemplateTask"/> entity (US-ONB-001). Maps to the
/// "onboarding_template_task" table with snake_case naming. Tenant isolation is via the global query
/// filter in AppDbContext + TenantInterceptor (no Postgres RLS). The responsible_role enum is stored as
/// a string; responsible_user_id is a cross-module reference (no hard FK), validated in the service.
/// </summary>
public sealed class OnboardingTemplateTaskConfiguration : IEntityTypeConfiguration<OnboardingTemplateTask>
{
    public void Configure(EntityTypeBuilder<OnboardingTemplateTask> builder)
    {
        builder.ToTable("onboarding_template_task");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.TemplateId).IsRequired();

        builder.Property(t => t.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasMaxLength(1000);

        builder.Property(t => t.Category)
            .HasMaxLength(100);

        // Stored as string for readability/forward-compat (matches the codebase's varchar enum pattern).
        builder.Property(t => t.ResponsibleRole)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(t => t.ResponsibleUserId);

        builder.Property(t => t.DueOffsetDays).IsRequired();
        builder.Property(t => t.IsMandatory).HasDefaultValue(false).IsRequired();
        // US-ONB-003 AC-4: document-required flag, propagated to assigned task instances.
        builder.Property(t => t.RequiresDocument).HasDefaultValue(false).IsRequired();
        builder.Property(t => t.SortOrder).IsRequired();

        builder.Property(t => t.IsDeleted).HasDefaultValue(false).IsRequired();

        // The Template reverse navigation + FK is configured on OnboardingChecklistTemplateConfiguration
        // via HasMany(t => t.Tasks).WithOne(t => t.Template!).

        // Ordered read of a template's tasks (by template, then sort order).
        builder.HasIndex(t => new { t.TenantId, t.TemplateId, t.SortOrder });
    }
}
