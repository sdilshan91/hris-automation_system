using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id");

        builder.Property(a => a.EventType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.Detail)
            .HasMaxLength(2000);

        builder.Property(a => a.IpAddress)
            .HasMaxLength(50);

        builder.Property(a => a.UserAgent)
            .HasMaxLength(500);

        // Index for tenant-scoped audit queries
        builder.HasIndex(a => new { a.TenantId, a.CreatedAt });

        // Index for user-scoped audit queries
        builder.HasIndex(a => new { a.UserId, a.EventType, a.CreatedAt });
    }
}
