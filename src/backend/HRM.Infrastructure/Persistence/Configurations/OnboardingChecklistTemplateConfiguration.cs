using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="OnboardingChecklistTemplate"/> entity (US-ONB-001). Maps to
/// the "onboarding_checklist_template" table with snake_case naming. Tenant isolation is via the global
/// query filter in AppDbContext + TenantInterceptor (no Postgres RLS — same as every other module).
/// Applicable departments/job titles are stored as Postgres uuid[] columns (universal = empty array).
/// </summary>
public sealed class OnboardingChecklistTemplateConfiguration : IEntityTypeConfiguration<OnboardingChecklistTemplate>
{
    public void Configure(EntityTypeBuilder<OnboardingChecklistTemplate> builder)
    {
        builder.ToTable("onboarding_checklist_template");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.TemplateName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasMaxLength(2000);

        // US-ONB-005 FR-1: onboarding vs offboarding discriminator, stored as a string (default Onboarding).
        builder.Property(t => t.Kind)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(HRM.Domain.Enums.ChecklistKind.Onboarding)
            .IsRequired();

        // FR-4: scoping arrays. Empty = universal. uuid[] on Postgres; the InMemory test provider stores
        // the List<Guid> directly.
        builder.Property(t => t.ApplicableDepartments)
            .HasColumnType("uuid[]")
            .IsRequired();

        builder.Property(t => t.ApplicableJobTitles)
            .HasColumnType("uuid[]")
            .IsRequired();

        builder.Property(t => t.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(t => t.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // BR-1 / AC-3: template name is unique per tenant. Partial — excludes soft-deleted rows so a
        // deleted name can be reused (BR-5).
        builder.HasIndex(t => new { t.TenantId, t.TemplateName })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // Tenant-scoped list read for the template list view (+ active filter for the assign dropdown).
        builder.HasIndex(t => new { t.TenantId, t.IsActive });

        builder.HasMany(t => t.Tasks)
            .WithOne(t => t.Template!)
            .HasForeignKey(t => t.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
