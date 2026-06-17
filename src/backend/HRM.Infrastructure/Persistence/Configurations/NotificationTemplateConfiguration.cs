using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the tenant-override <see cref="NotificationTemplate"/> (US-NTF-002 §7). Maps to
/// "notification_template" (snake_case). Tenant-scoped — the AppDbContext global query filter isolates rows by
/// the current tenant (AC-5/NFR-2). A partial unique index keeps at most one LIVE override per (event, language)
/// within a tenant; a reset soft-deletes the row so a fresh override can be created afterwards.
/// </summary>
public sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("notification_template");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id");

        builder.Property(t => t.EventKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.Language)
            .HasMaxLength(10)
            .HasDefaultValue("en")
            .IsRequired();

        builder.Property(t => t.Subject)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.BodyHtml)
            .IsRequired();

        builder.Property(t => t.BodyText)
            .IsRequired();

        builder.Property(t => t.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(t => t.Version)
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(t => t.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // AC-3/AC-5: at most one LIVE override per (tenant, event, language). Soft-deleted rows are excluded so a
        // reset (soft-delete) followed by a new override does not collide.
        builder.HasIndex(t => new { t.TenantId, t.EventKey, t.Language })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_notification_template_tenant_event_language");
    }
}
