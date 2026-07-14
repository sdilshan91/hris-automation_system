using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="StatutoryRule"/> entity (US-PAY-006). Maps to the
/// "statutory_rule" table with snake_case naming. Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor (Postgres RLS is the deferred US-PLT-002).
/// </summary>
public sealed class StatutoryRuleConfiguration : IEntityTypeConfiguration<StatutoryRule>
{
    public void Configure(EntityTypeBuilder<StatutoryRule> builder)
    {
        builder.ToTable("statutory_rule");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        // Enum stored as string (matches the codebase varchar pattern + the global JsonStringEnumConverter).
        builder.Property(r => r.RuleType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.RuleName).HasMaxLength(100).IsRequired();
        builder.Property(r => r.CountryCode).HasMaxLength(5).IsRequired();
        builder.Property(r => r.FiscalYear).HasMaxLength(10).IsRequired();

        builder.Property(r => r.EffectiveFrom).IsRequired();
        builder.Property(r => r.EffectiveTo);

        builder.Property(r => r.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(r => r.IsDeleted).HasDefaultValue(false).IsRequired();

        // TAX-3: cumulative (YTD) income-tax flag. Defaults false so existing rules keep monthly behaviour.
        builder.Property(r => r.IsCumulative).HasDefaultValue(false).IsRequired();

        builder.HasMany(r => r.TaxSlabs)
            .WithOne(s => s.Rule!)
            .HasForeignKey(s => s.StatutoryRuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // TAX-2: configurable income-tax exemptions are owned children (mirrors TaxSlabs).
        builder.HasMany(r => r.Exemptions)
            .WithOne(e => e.Rule!)
            .HasForeignKey(e => e.StatutoryRuleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.SocialSecurityRule)
            .WithOne(s => s.Rule!)
            .HasForeignKey<SocialSecurityRule>(s => s.StatutoryRuleId)
            .OnDelete(DeleteBehavior.Cascade);

        // Resolve-for-period lookups filter by tenant + type + fiscal year + effective range (FR-4).
        // Multi-country tax foundation: the (tenant, type, fiscal_year) index now includes country_code AND
        // effective_from and is UNIQUE — integrity: forbids a TRUE duplicate (identical type/country/FY AND the
        // same effective-from) so two countries' rules of the same type never collide, WHILE still allowing
        // multiple effective-dated VERSIONS within one fiscal year (e.g. a mid-year rate change → two rules,
        // same type/country/FY, different effective-from), which the resolver's SelectEffective picks between.
        // Filtered on is_deleted = false so a soft-deleted rule does not block re-creating the same version.
        builder.HasIndex(r => new { r.TenantId, r.RuleType, r.CountryCode, r.FiscalYear, r.EffectiveFrom })
            .IsUnique()
            .HasFilter("is_deleted = false");
        builder.HasIndex(r => new { r.TenantId, r.RuleType, r.EffectiveFrom });
    }
}
