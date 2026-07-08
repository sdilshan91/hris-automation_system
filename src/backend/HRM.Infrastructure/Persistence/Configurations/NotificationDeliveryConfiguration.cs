using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="NotificationDelivery"/> (US-NTF-006). Maps to the "notification_delivery"
/// table (snake_case). Tenant isolation is via the global query filter in AppDbContext + TenantInterceptor. The
/// channel/status enums are stored as strings, mirroring <see cref="OnboardingNotificationOutboxConfiguration"/>.
/// </summary>
public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_delivery");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Channel)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Attempts).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.Property(x => x.SentAt);

        builder.Property(x => x.NotificationType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.EventKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.RecipientUserId).IsRequired();
        builder.Property(x => x.RecipientEmail).HasMaxLength(320);
        builder.Property(x => x.Subject).HasMaxLength(500);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // Operator/worker scans: rows per tenant by delivery state, and by recipient.
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => x.RecipientUserId);
    }
}
