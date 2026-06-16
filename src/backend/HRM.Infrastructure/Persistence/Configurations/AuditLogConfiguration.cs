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

        // ── Structured audit columns (US-PAY-012, technical doc §19.9) — all nullable/additive ──
        builder.Property(a => a.Action)
            .HasMaxLength(100);

        builder.Property(a => a.ResourceType)
            .HasMaxLength(50);

        builder.Property(a => a.ResourceId)
            .HasMaxLength(100);

        // Before/After hold JSON. jsonb on PostgreSQL (the production provider); plain text on InMemory.
        builder.Property(a => a.Before)
            .HasColumnType("jsonb");
        builder.Property(a => a.After)
            .HasColumnType("jsonb");

        builder.Property(a => a.ActorEmployeeNo)
            .HasMaxLength(50);

        builder.Property(a => a.TraceId)
            .HasMaxLength(100);

        // ── Impersonator attribution columns (US-ADM-003 FR-3/AC-2) — nullable/additive ──
        // ImpersonatorUserId, ImpersonationSessionId (both Guid?) and IsImpersonationAction (bool, default false)
        // are mapped by convention (snake_case). Index supports the "who-did-what-while-impersonating" audit view.
        builder.HasIndex(a => a.ImpersonationSessionId);

        // Index for tenant-scoped audit queries
        builder.HasIndex(a => new { a.TenantId, a.CreatedAt });

        // Index for user-scoped audit queries
        builder.HasIndex(a => new { a.UserId, a.EventType, a.CreatedAt });

        // US-PAY-012 FR-4: payroll audit-trail filtering by tenant + resource type + time (the trail query
        // filters on resource_type and orders by timestamp). NFR-3 BRIN-on-timestamp is a Postgres-specific
        // follow-up; a btree composite covers the common filter cheaply for now.
        builder.HasIndex(a => new { a.TenantId, a.ResourceType, a.CreatedAt });
    }
}
