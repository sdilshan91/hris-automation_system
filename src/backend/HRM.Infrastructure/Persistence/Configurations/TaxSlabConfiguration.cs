using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="TaxSlab"/> entity (US-PAY-006). Maps to the "tax_slab" table
/// with snake_case naming. Tenant isolation is via the global query filter + TenantInterceptor.
/// </summary>
public sealed class TaxSlabConfiguration : IEntityTypeConfiguration<TaxSlab>
{
    public void Configure(EntityTypeBuilder<TaxSlab> builder)
    {
        builder.ToTable("tax_slab");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.StatutoryRuleId).IsRequired();

        // Money — numeric(18,2); rate — numeric(5,2) (§7/§10).
        builder.Property(s => s.SlabFrom).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(s => s.SlabTo).HasColumnType("numeric(18,2)");
        builder.Property(s => s.RatePercentage).HasColumnType("numeric(5,2)").IsRequired();

        builder.Property(s => s.OrderIndex).IsRequired();
        builder.Property(s => s.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.StatutoryRuleId, s.OrderIndex });
    }
}
