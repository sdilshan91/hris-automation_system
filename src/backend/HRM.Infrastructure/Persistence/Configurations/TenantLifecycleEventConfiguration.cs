using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="TenantLifecycleEvent"/> (US-ADM-001). Cross-tenant/system table
/// "tenant_lifecycle_events" — NOT tenant-scoped (no global query filter): the System Admin reads/writes
/// lifecycle history across all tenants.
/// </summary>
public sealed class TenantLifecycleEventConfiguration : IEntityTypeConfiguration<TenantLifecycleEvent>
{
    public void Configure(EntityTypeBuilder<TenantLifecycleEvent> builder)
    {
        builder.ToTable("tenant_lifecycle_events");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.TenantId)
            .IsRequired();

        builder.HasIndex(e => e.TenantId)
            .HasDatabaseName("ix_tenant_lifecycle_events_tenant_id");

        builder.Property(e => e.EventType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.DetailJson)
            .HasColumnType("jsonb");

        builder.Property(e => e.CreatedBy)
            .HasMaxLength(256);
    }
}
