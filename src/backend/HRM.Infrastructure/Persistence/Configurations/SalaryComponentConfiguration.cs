using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the SalaryComponent entity (US-PAY-001). Maps to the "salary_component"
/// table with snake_case naming. Tenant isolation is via the global query filter in AppDbContext +
/// TenantInterceptor (this codebase does not use Postgres RLS — RLS is the deferred US-PLT-002).
/// </summary>
public sealed class SalaryComponentConfiguration : IEntityTypeConfiguration<SalaryComponent>
{
    public void Configure(EntityTypeBuilder<SalaryComponent> builder)
    {
        builder.ToTable("salary_component");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.Code)
            .HasMaxLength(50)
            .IsRequired();

        // Enums stored as string (matches the codebase's varchar status pattern + the global
        // JsonStringEnumConverter so DB values stay human-readable and API-aligned).
        builder.Property(c => c.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.CalculationMethod)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Money / percentage — numeric(18,2) per §10 to avoid floating-point precision issues.
        builder.Property(c => c.DefaultValue).HasColumnType("numeric(18,2)");

        builder.Property(c => c.FormulaExpression).HasColumnType("text");

        builder.Property(c => c.IsTaxable).HasDefaultValue(true).IsRequired();
        builder.Property(c => c.IsStatutory).HasDefaultValue(false).IsRequired();
        builder.Property(c => c.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(c => c.ProcessingOrder).HasDefaultValue(0).IsRequired();

        builder.Property(c => c.IsDeleted).HasDefaultValue(false).IsRequired();

        // BR-2: component code is unique per tenant. Partial — excludes soft-deleted rows so a deleted
        // code can be reused (two tenants may share the same code — TenantId is in the key).
        builder.HasIndex(c => new { c.TenantId, c.Code })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(c => new { c.TenantId, c.Type });
    }
}
