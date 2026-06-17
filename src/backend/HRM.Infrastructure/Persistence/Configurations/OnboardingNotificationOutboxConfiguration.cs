using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="OnboardingNotificationOutbox"/> (US-ONB-002 NFR-3). Maps to the
/// "onboarding_notification_outbox" table (snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor. Intent rows are written in the assignment transaction; a Hangfire
/// worker reads pending rows and dispatches via INotificationDispatcher. The role/status enums are stored
/// as strings; payload is a free-text JSON column.
/// </summary>
public sealed class OnboardingNotificationOutboxConfiguration : IEntityTypeConfiguration<OnboardingNotificationOutbox>
{
    public void Configure(EntityTypeBuilder<OnboardingNotificationOutbox> builder)
    {
        builder.ToTable("onboarding_notification_outbox");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ChecklistInstanceId).IsRequired();
        builder.Property(x => x.RecipientUserId).IsRequired();

        builder.Property(x => x.RecipientRole)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.NotificationType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.DispatchedAt);
        builder.Property(x => x.AttemptCount).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LastError).HasMaxLength(2000);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // Worker scan: pending rows per tenant (the dispatch job filters status = Pending).
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasIndex(x => x.ChecklistInstanceId);
    }
}
