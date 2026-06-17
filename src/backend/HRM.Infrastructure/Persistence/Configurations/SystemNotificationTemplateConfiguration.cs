using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="SystemNotificationTemplate"/> (US-NTF-002 §7). Platform/system table
/// "system_notification_template" — NOT tenant-scoped (no global query filter is applied in AppDbContext), like
/// "subscription_plans". One row per (event_key, language); seeded by DbInitializer.
/// </summary>
public sealed class SystemNotificationTemplateConfiguration
    : IEntityTypeConfiguration<SystemNotificationTemplate>
{
    public void Configure(EntityTypeBuilder<SystemNotificationTemplate> builder)
    {
        builder.ToTable("system_notification_template");

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

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt);

        // BR-2: exactly one system default per event + language.
        builder.HasIndex(t => new { t.EventKey, t.Language })
            .IsUnique()
            .HasDatabaseName("ux_system_notification_template_event_language");
    }
}
