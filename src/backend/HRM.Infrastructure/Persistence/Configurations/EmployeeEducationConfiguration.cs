using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for EmployeeEducation (US-CHR-002 / DF-39).
/// </summary>
public sealed class EmployeeEducationConfiguration : IEntityTypeConfiguration<EmployeeEducation>
{
    public void Configure(EntityTypeBuilder<EmployeeEducation> builder)
    {
        builder.ToTable("employee_education");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Institution)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Degree)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.FieldOfStudy)
            .HasMaxLength(200);

        builder.Property(e => e.StartYear)
            .HasMaxLength(10);

        builder.Property(e => e.EndYear)
            .HasMaxLength(10);

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // Index for efficient queries by employee
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId })
            .HasDatabaseName("ix_employee_education_tenant_employee");
    }
}
