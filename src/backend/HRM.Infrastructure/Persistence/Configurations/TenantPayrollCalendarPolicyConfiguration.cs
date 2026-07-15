using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="TenantPayrollCalendarPolicy"/> (US-ATT-011 AC-4 / CAL-5). Maps to
/// "tenant_payroll_calendar_policy" with snake_case naming. Tenant isolation is via the AppDbContext global
/// query filter + TenantInterceptor + the dormant Postgres RLS policy shipped in the migration.
/// </summary>
public sealed class TenantPayrollCalendarPolicyConfiguration : IEntityTypeConfiguration<TenantPayrollCalendarPolicy>
{
    public void Configure(EntityTypeBuilder<TenantPayrollCalendarPolicy> builder)
    {
        builder.ToTable("tenant_payroll_calendar_policy");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.EffectiveFrom).IsRequired();

        // DEFAULT FALSE — money-critical. A raw INSERT that omits the column must NOT opt a tenant in.
        builder.Property(p => p.ExcludeHolidaysFromWorkingDays).HasDefaultValue(false).IsRequired();

        builder.Property(p => p.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(p => p.IsDeleted).HasDefaultValue(false).IsRequired();

        // Effective-version lookup: resolve the latest EffectiveFrom <= the pay-period date for a tenant.
        builder.HasIndex(p => new { p.TenantId, p.EffectiveFrom });
    }
}
