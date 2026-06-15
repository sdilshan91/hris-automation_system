using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the SalaryStructure entity (US-PAY-001). Maps to the "salary_structure"
/// table with snake_case naming. Tenant isolation via the global query filter + TenantInterceptor.
/// </summary>
public sealed class SalaryStructureConfiguration : IEntityTypeConfiguration<SalaryStructure>
{
    public void Configure(EntityTypeBuilder<SalaryStructure> builder)
    {
        builder.ToTable("salary_structure");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(s => s.Description).HasColumnType("text");

        builder.Property(s => s.EffectiveFrom).IsRequired();

        builder.Property(s => s.IsDefault).HasDefaultValue(false).IsRequired();
        builder.Property(s => s.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(s => s.IsDeleted).HasDefaultValue(false).IsRequired();

        // Owned link rows are loaded/managed explicitly via the dedicated DbSet, not as a mapped nav,
        // to keep the structure aggregate simple; ignore the convenience collection on the entity.
        builder.Ignore(s => s.Components);

        // BR-2: structure code is unique per tenant (partial — excludes soft-deleted).
        builder.HasIndex(s => new { s.TenantId, s.Code })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // BR-1: at most one default structure per tenant — partial unique index over the default flag
        // (only one row per tenant may have is_default = true, ignoring soft-deleted rows).
        builder.HasIndex(s => s.TenantId)
            .IsUnique()
            .HasFilter("is_default = true AND is_deleted = false")
            .HasDatabaseName("ix_salary_structure_one_default_per_tenant");
    }
}
