using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the JobTitle entity (US-CHR-005).
/// Maps to the "job_titles" table with snake_case naming convention.
/// </summary>
public sealed class JobTitleConfiguration : IEntityTypeConfiguration<JobTitle>
{
    public void Configure(EntityTypeBuilder<JobTitle> builder)
    {
        builder.ToTable("job_titles");

        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id)
            .HasColumnName("id");

        builder.Property(j => j.TitleName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(j => j.Description)
            .HasMaxLength(1000);

        builder.Property(j => j.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(j => j.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // GradeId is a nullable UUID column WITHOUT a hard FK constraint.
        // The Grade entity does not exist yet (Payroll module builds it).
        // TODO(payroll): wire GradeId FK to Grade entity once it exists.
        builder.Property(j => j.GradeId);

        // Unique constraint: title_name per tenant (FR-2, BR-1)
        // Partial index excludes soft-deleted records so deactivated titles
        // don't block reuse of the same name.
        builder.HasIndex(j => new { j.TenantId, j.TitleName })
            .IsUnique()
            .HasFilter("is_deleted = false");
    }
}
