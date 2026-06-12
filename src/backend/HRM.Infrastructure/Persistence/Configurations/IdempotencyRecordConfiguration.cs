using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for IdempotencyRecord (US-CHR-009 NFR-3).
/// Stores idempotency keys per tenant to prevent duplicate write operations.
/// </summary>
public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.IdempotencyKey)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.OperationName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.ResponseJson)
            .HasColumnType("jsonb");

        builder.Property(e => e.ResponseStatusCode)
            .IsRequired();

        builder.Property(e => e.ExpiresAt)
            .IsRequired();

        // Unique constraint: one idempotency key per tenant per operation
        builder.HasIndex(e => new { e.TenantId, e.IdempotencyKey, e.OperationName })
            .IsUnique()
            .HasDatabaseName("ix_idempotency_records_tenant_key_operation");

        // Index for cleanup job (find expired records)
        builder.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("ix_idempotency_records_expires_at");
    }
}
