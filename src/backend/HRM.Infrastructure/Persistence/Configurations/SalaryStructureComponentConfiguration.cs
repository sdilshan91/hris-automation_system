using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the SalaryStructureComponent junction (US-PAY-001 FR-3). Maps to the
/// "salary_structure_component" table with snake_case naming. Tenant isolation via the global query
/// filter + TenantInterceptor.
/// </summary>
public sealed class SalaryStructureComponentConfiguration : IEntityTypeConfiguration<SalaryStructureComponent>
{
    public void Configure(EntityTypeBuilder<SalaryStructureComponent> builder)
    {
        builder.ToTable("salary_structure_component");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.SalaryStructureId).IsRequired();
        builder.Property(x => x.SalaryComponentId).IsRequired();

        builder.Property(x => x.OverrideValue).HasColumnType("numeric(18,2)");
        builder.Property(x => x.OverrideFormula).HasColumnType("text");

        builder.Property(x => x.ProcessingOrder).IsRequired();
        builder.Property(x => x.IsMandatory).HasDefaultValue(false).IsRequired();

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.Ignore(x => x.Structure);
        builder.Ignore(x => x.Component);

        // A component appears at most once per structure (partial — excludes soft-deleted links).
        builder.HasIndex(x => new { x.SalaryStructureId, x.SalaryComponentId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(x => x.SalaryStructureId);
        builder.HasIndex(x => x.SalaryComponentId);
    }
}
