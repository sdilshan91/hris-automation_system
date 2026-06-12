using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the Department entity (US-CHR-004).
/// Maps to the "departments" table with snake_case naming convention.
/// </summary>
public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("departments");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id");

        builder.Property(d => d.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(d => d.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(d => d.Description)
            .HasMaxLength(1000);

        builder.Property(d => d.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(d => d.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ManagerId is a nullable UUID column WITHOUT a hard FK constraint.
        // The Employee entity does not exist yet (US-CHR-001 builds it).
        // TODO(US-CHR-001): wire ManagerId FK to Employee entity once it exists.
        builder.Property(d => d.ManagerId);

        // Unique constraint: department name per tenant (FR-2, BR-1)
        builder.HasIndex(d => new { d.TenantId, d.Name })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // Unique constraint: department code per tenant
        builder.HasIndex(d => new { d.TenantId, d.Code })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // Self-referencing FK for hierarchy (FR-3)
        builder.HasOne(d => d.ParentDepartment)
            .WithMany(d => d.ChildDepartments)
            .HasForeignKey(d => d.ParentDepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
