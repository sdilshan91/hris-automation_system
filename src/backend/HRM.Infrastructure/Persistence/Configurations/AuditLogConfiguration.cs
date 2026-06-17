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

        // ── US-ADM-008 NFR-2: tenant-audit-log query indexes ──────────────────────────────────────────
        // The list/export filter by tenant + (date | actor | action | resource), reverse-chronological. The
        // existing (tenant_id, created_at) index above covers the default reverse-chronological page; these add
        // the remaining filtered access paths the story calls out. (tenant_id, resource_type, resource_id)
        // serves the "all events for one resource" drill-down (extends the PAY-012 resource_type index above
        // with resource_id). Postgres applies DESC on created_at at scan time; the btree covers it either way.
        builder.HasIndex(a => new { a.TenantId, a.UserId });
        builder.HasIndex(a => new { a.TenantId, a.Action });
        builder.HasIndex(a => new { a.TenantId, a.ResourceType, a.ResourceId });
    }
}
