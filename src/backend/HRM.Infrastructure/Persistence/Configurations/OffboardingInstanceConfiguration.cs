using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="OffboardingInstance"/> (US-ONB-005). Maps to the
/// "offboarding_instance" table (snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor (no Postgres RLS — same as every other module). The reason + status
/// enums are stored as strings; employee_id and template_id are cross-module refs (no hard FK).
/// </summary>
public sealed class OffboardingInstanceConfiguration : IEntityTypeConfiguration<OffboardingInstance>
{
    public void Configure(EntityTypeBuilder<OffboardingInstance> builder)
    {
        builder.ToTable("offboarding_instance");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.EmployeeId).IsRequired();
        builder.Property(x => x.TemplateId);

        builder.Property(x => x.TemplateName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.LastWorkingDay).IsRequired();

        builder.Property(x => x.Reason)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.InitiatedByUserId);
        builder.Property(x => x.CompletedAt);
        builder.Property(x => x.CompletedByUserId);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // GET ?employeeId={id} — the employee's current offboarding (BR: at most one in-progress per employee).
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId });

        builder.HasMany(x => x.Tasks)
            .WithOne(t => t.OffboardingInstance!)
            .HasForeignKey(t => t.OffboardingInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
