using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="TenantApiUsage"/> (US-PLT-004 API-call counter). Maps to
/// "tenant_api_usage" (snake_case) with a UNIQUE (tenant_id, year_month) index — the target of the flusher's
/// <c>INSERT … ON CONFLICT (tenant_id, year_month) DO UPDATE</c> atomic upsert.
/// </summary>
public sealed class TenantApiUsageConfiguration : IEntityTypeConfiguration<TenantApiUsage>
{
    public void Configure(EntityTypeBuilder<TenantApiUsage> builder)
    {
        builder.ToTable("tenant_api_usage");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id).HasColumnName("id");
        builder.Property(u => u.TenantId).IsRequired();
        builder.Property(u => u.YearMonth).IsRequired();
        builder.Property(u => u.CallCount).IsRequired().HasDefaultValue(0L);

        // The upsert's ON CONFLICT arbiter: exactly one row per tenant-month.
        builder.HasIndex(u => new { u.TenantId, u.YearMonth }).IsUnique();
    }
}
