using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="TenantFnFPolicy"/> (ISSUE-294 Phase 1). Maps to "tenant_fnf_policy"
/// with snake_case naming. Tenant isolation is via the AppDbContext global query filter + TenantInterceptor +
/// the dormant Postgres RLS policy shipped in the migration.
/// </summary>
public sealed class TenantFnFPolicyConfiguration : IEntityTypeConfiguration<TenantFnFPolicy>
{
    public void Configure(EntityTypeBuilder<TenantFnFPolicy> builder)
    {
        builder.ToTable("tenant_fnf_policy");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.EffectiveFrom).IsRequired();
        builder.Property(p => p.IncludeProRatedFinalPay).HasDefaultValue(true).IsRequired();
        builder.Property(p => p.IncludeStatutory).HasDefaultValue(true).IsRequired();
        builder.Property(p => p.IncludeLeaveEncashment).HasDefaultValue(true).IsRequired();
        builder.Property(p => p.FinalPeriodOwnedBySettlement).HasDefaultValue(true).IsRequired();
        builder.Property(p => p.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(p => p.IsDeleted).HasDefaultValue(false).IsRequired();

        // Effective-version lookup: resolve the latest EffectiveFrom <= LWD for a tenant.
        builder.HasIndex(p => new { p.TenantId, p.EffectiveFrom });
    }
}
