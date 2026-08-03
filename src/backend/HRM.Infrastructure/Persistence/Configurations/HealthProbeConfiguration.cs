using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// SYSTEM-scope table (no <c>tenant_id</c> — like <c>encryption_key_activation</c>/<c>data_protection_keys</c>,
/// so the new-tenant-table dormant-RLS-policy rule does NOT apply and <c>RlsIsolationPostgresTests</c> exempts
/// it). One row per readiness probe; the SLA uptime percentage is aggregated from these.
/// </summary>
public sealed class HealthProbeConfiguration : IEntityTypeConfiguration<HealthProbe>
{
    public void Configure(EntityTypeBuilder<HealthProbe> builder)
    {
        builder.ToTable("health_probe");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.ObservedAtUtc).IsRequired();
        builder.Property(p => p.IsHealthy).IsRequired();
        builder.Property(p => p.Status).IsRequired().HasMaxLength(32);
        builder.Property(p => p.DurationMs).IsRequired();

        // Every read is "probes since X" (uptime window) and every prune is "probes before X" (retention),
        // so the observation instant is the only access path that matters.
        builder.HasIndex(p => p.ObservedAtUtc);
    }
}
