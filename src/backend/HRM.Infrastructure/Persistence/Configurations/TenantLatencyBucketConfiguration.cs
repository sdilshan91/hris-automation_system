using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="TenantLatencyBucket"/> (TC-ADM-002-14/-16). Maps to
/// "tenant_latency_bucket" with a UNIQUE (tenant_id, hour_utc, bucket_index) index — the arbiter for the
/// flusher's <c>INSERT … ON CONFLICT … DO UPDATE count = count + EXCLUDED.count</c> atomic upsert, mirroring
/// <see cref="TenantApiUsageConfiguration"/>.
/// </summary>
public sealed class TenantLatencyBucketConfiguration : IEntityTypeConfiguration<TenantLatencyBucket>
{
    public void Configure(EntityTypeBuilder<TenantLatencyBucket> builder)
    {
        builder.ToTable("tenant_latency_bucket");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");
        builder.Property(b => b.TenantId).IsRequired();
        builder.Property(b => b.HourUtc).IsRequired();
        builder.Property(b => b.BucketIndex).IsRequired();
        builder.Property(b => b.Count).IsRequired().HasDefaultValue(0L);

        // The upsert's ON CONFLICT arbiter: exactly one row per (tenant, hour, bucket).
        builder.HasIndex(b => new { b.TenantId, b.HourUtc, b.BucketIndex }).IsUnique();

        // Reads are always "this tenant's last N hours" and the prune is "older than X" — both hit this.
        builder.HasIndex(b => b.HourUtc);
    }
}
