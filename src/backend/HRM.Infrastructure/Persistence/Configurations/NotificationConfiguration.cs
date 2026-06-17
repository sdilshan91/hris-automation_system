using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="Notification"/> entity (US-NTF-001 §7).
/// Maps to the "notification" table with the snake_case naming convention.
/// </summary>
public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notification");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .HasColumnName("id");

        builder.Property(n => n.UserId)
            .IsRequired();

        builder.Property(n => n.Type)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(n => n.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(n => n.Message)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(n => n.ResourceType)
            .HasMaxLength(50);

        builder.Property(n => n.ResourceId)
            .HasMaxLength(100);

        builder.Property(n => n.IsRead)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(n => n.ReadAt);

        builder.Property(n => n.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // FR-6 / AC-3: the panel lists a user's notifications most-recent-first. This composite index
        // backs the per-user, created-at-DESC paged read (the dominant query path).
        builder.HasIndex(n => new { n.TenantId, n.UserId, n.CreatedAt })
            .HasDatabaseName("ix_notification_tenant_user_created");

        // FR-5: the unread-count badge filters by (tenant, user, is_read=false). A partial index keeps
        // it tight since the hot path only ever counts unread rows.
        builder.HasIndex(n => new { n.TenantId, n.UserId })
            .HasFilter("is_read = false AND is_deleted = false")
            .HasDatabaseName("ix_notification_tenant_user_unread");
    }
}
