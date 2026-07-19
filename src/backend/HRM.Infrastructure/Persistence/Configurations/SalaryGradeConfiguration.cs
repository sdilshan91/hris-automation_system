using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the SalaryGrade entity (Payroll domain, ISSUE-021).
/// Maps to the "salary_grades" table with snake_case naming convention.
/// </summary>
public sealed class SalaryGradeConfiguration : IEntityTypeConfiguration<SalaryGrade>
{
    public void Configure(EntityTypeBuilder<SalaryGrade> builder)
    {
        builder.ToTable("salary_grades");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.Id)
            .HasColumnName("id");

        builder.Property(g => g.Code)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(g => g.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(g => g.MinAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(g => g.MidAmount)
            .HasPrecision(18, 2);

        builder.Property(g => g.MaxAmount)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(g => g.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(g => g.Description)
            .HasMaxLength(500);

        builder.Property(g => g.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(g => g.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // Unique constraint: code per tenant. Partial index excludes soft-deleted records so
        // deactivated/deleted grades don't block reuse of the same code. Case-insensitive uniqueness
        // is enforced at the service layer (ToLower comparison), mirroring LeaveType/JobTitle.
        builder.HasIndex(g => new { g.TenantId, g.Code })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_salary_grades_tenant_id_code");
    }
}
