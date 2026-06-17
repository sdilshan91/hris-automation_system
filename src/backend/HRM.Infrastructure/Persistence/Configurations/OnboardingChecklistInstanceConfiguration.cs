using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="OnboardingChecklistInstance"/> (US-ONB-002). Maps to the
/// "onboarding_checklist_instance" table (snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor (no Postgres RLS — same as every other module). The status enum is
/// stored as a string. BR-2 (at most one active checklist per employee) is enforced in the service.
/// </summary>
public sealed class OnboardingChecklistInstanceConfiguration : IEntityTypeConfiguration<OnboardingChecklistInstance>
{
    public void Configure(EntityTypeBuilder<OnboardingChecklistInstance> builder)
    {
        builder.ToTable("onboarding_checklist_instance");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.EmployeeId).IsRequired();
        builder.Property(x => x.TemplateId).IsRequired();

        builder.Property(x => x.TemplateName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.StartDate).IsRequired();
        builder.Property(x => x.Version).HasDefaultValue(1).IsRequired();
        builder.Property(x => x.AssignedByUserId);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // BR-2: fast lookup of the employee's active checklist (the active-uniqueness is enforced in the
        // service before insert). Scoped by tenant + employee + status.
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });

        builder.HasMany(x => x.Tasks)
            .WithOne(t => t.ChecklistInstance!)
            .HasForeignKey(t => t.ChecklistInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
