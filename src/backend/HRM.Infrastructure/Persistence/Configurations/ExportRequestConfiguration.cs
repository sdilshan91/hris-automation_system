using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="ExportRequest"/> entity (US-ADM-010). Maps to "export_requests"
/// (snake_case). Tenant isolation via the global query filter + TenantInterceptor. Indexes support the two
/// hot reads: the rate-limit / status gate (by tenant + status) and the history list (by tenant + requested_at).
/// </summary>
public sealed class ExportRequestConfiguration : IEntityTypeConfiguration<ExportRequest>
{
    public void Configure(EntityTypeBuilder<ExportRequest> builder)
    {
        builder.ToTable("export_requests");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.Scope).HasMaxLength(2000).IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.RequestedByUserId).IsRequired();
        builder.Property(e => e.RequestedAt).IsRequired();
        builder.Property(e => e.FilePath).HasMaxLength(1000);
        builder.Property(e => e.Error).HasMaxLength(2000);
        builder.Property(e => e.InitiatedBySystemAdmin).HasDefaultValue(false).IsRequired();
        builder.Property(e => e.IsDeleted).HasDefaultValue(false).IsRequired();

        // ManifestJson can be large — stored as jsonb on PostgreSQL.
        builder.Property(e => e.ManifestJson).HasColumnType("jsonb");

        // Rate-limit + status gate (BR-5): "is there a Queued/Processing export for this tenant?".
        builder.HasIndex(e => new { e.TenantId, e.Status })
            .HasDatabaseName("ix_export_requests_tenant_status");

        // History list (FR-9 / §8): newest-first per tenant.
        builder.HasIndex(e => new { e.TenantId, e.RequestedAt })
            .HasDatabaseName("ix_export_requests_tenant_requested_at");
    }
}
