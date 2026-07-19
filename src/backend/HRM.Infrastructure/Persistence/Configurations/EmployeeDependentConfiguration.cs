using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for EmployeeDependent (US-CHR-002 / DF-39).
/// DateOfBirth is DateOnly → PostgreSQL <c>date</c> column.
/// </summary>
public sealed class EmployeeDependentConfiguration : IEntityTypeConfiguration<EmployeeDependent>
{
    public void Configure(EntityTypeBuilder<EmployeeDependent> builder)
    {
        builder.ToTable("employee_dependents");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Relationship)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.DateOfBirth)
            .HasColumnType("date");

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // Index for efficient queries by employee
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId })
            .HasDatabaseName("ix_employee_dependents_tenant_employee");
    }
}
