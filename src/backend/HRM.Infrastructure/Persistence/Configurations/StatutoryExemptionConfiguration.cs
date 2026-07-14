using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="StatutoryExemption"/> entity (TAX-2). Maps to the
/// "statutory_exemption" table with snake_case naming. Tenant isolation is via the global query filter +
/// TenantInterceptor (+ Postgres RLS). Mirrors <see cref="TaxSlabConfiguration"/>.
/// </summary>
public sealed class StatutoryExemptionConfiguration : IEntityTypeConfiguration<StatutoryExemption>
{
    public void Configure(EntityTypeBuilder<StatutoryExemption> builder)
    {
        builder.ToTable("statutory_exemption");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.StatutoryRuleId).IsRequired();

        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();

        // Enum stored as string (matches the codebase varchar pattern + the global JsonStringEnumConverter).
        builder.Property(e => e.CalculationType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // numeric(18,4) — must hold both a large flat currency amount AND a precise percentage.
        builder.Property(e => e.Value).HasColumnType("numeric(18,4)").IsRequired();
        builder.Property(e => e.MaxAmount).HasColumnType("numeric(18,4)");

        builder.Property(e => e.ComponentId);
        builder.Property(e => e.IsAnnual).HasDefaultValue(false).IsRequired();

        builder.Property(e => e.OrderIndex).IsRequired();
        builder.Property(e => e.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.StatutoryRuleId, e.OrderIndex });
    }
}
