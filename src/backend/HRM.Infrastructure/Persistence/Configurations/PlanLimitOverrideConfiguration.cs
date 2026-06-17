using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="PlanLimitOverride"/> (US-ADM-009 FR-4). Platform/system table
/// "plan_limit_overrides" — NOT tenant-scoped (no global query filter applied in AppDbContext); it is managed
/// only from the system-admin console and references the tenant by id.
/// </summary>
public sealed class PlanLimitOverrideConfiguration : IEntityTypeConfiguration<PlanLimitOverride>
{
    public void Configure(EntityTypeBuilder<PlanLimitOverride> builder)
    {
        builder.ToTable("plan_limit_overrides");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id");

        builder.Property(o => o.TenantId)
            .IsRequired();

        builder.Property(o => o.LimitKey)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(o => o.Value);

        builder.Property(o => o.ExpiresAt);

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.Property(o => o.UpdatedAt);

        // One active override row per (tenant, limit_key); also the lookup index for resolution (FR-4).
        builder.HasIndex(o => new { o.TenantId, o.LimitKey })
            .IsUnique()
            .HasDatabaseName("ix_plan_limit_overrides_tenant_id_limit_key");
    }
}
