using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// SYSTEM-scope table (no <c>tenant_id</c> — like <c>tenants</c>/<c>data_protection_keys</c>, so the
/// new-tenant-table dormant-RLS-policy rule does NOT apply and <c>RlsIsolationPostgresTests</c> exempts it).
/// One row per field-encryption keyId: when it was first seen active (drives the key-age watchdog).
/// </summary>
public sealed class EncryptionKeyActivationConfiguration : IEntityTypeConfiguration<EncryptionKeyActivation>
{
    public void Configure(EntityTypeBuilder<EncryptionKeyActivation> builder)
    {
        builder.ToTable("encryption_key_activation");

        builder.HasKey(k => k.KeyId);

        builder.Property(k => k.KeyId)
            .HasMaxLength(128); // config keyIds are short operator-chosen labels (e.g. hrm-field-key-2)

        builder.Property(k => k.FirstSeenUtc)
            .IsRequired();
    }
}
