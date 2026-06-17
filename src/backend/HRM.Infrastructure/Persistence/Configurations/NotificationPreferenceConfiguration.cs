using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="NotificationPreference"/> entity (US-NTF-003 §7). Maps to the
/// "notification_preference" table with the snake_case naming convention. Tenant isolation is via the global
/// query filter in AppDbContext + TenantInterceptor (this codebase does not use Postgres RLS — RLS is the
/// deferred US-PLT-002).
/// </summary>
public sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("notification_preference");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.UserId)
            .IsRequired();

        // Enum stored as string (matches the codebase's varchar-enum pattern + the global
        // JsonStringEnumConverter so DB values stay human-readable and API-aligned).
        builder.Property(p => p.Category)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(p => p.ChannelInApp)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(p => p.ChannelEmail)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(p => p.IsMandatory)
            .HasDefaultValue(false)
            .IsRequired();

        // FR-9/BR-5: quiet-hours window stored as postgres `time`.
        builder.Property(p => p.QuietHoursStart)
            .HasColumnType("time");

        builder.Property(p => p.QuietHoursEnd)
            .HasColumnType("time");

        builder.Property(p => p.QuietHoursTimezone)
            .HasMaxLength(50);

        builder.Property(p => p.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // §7 / BR-4: unique per (tenant, user, category) — one saved override row per category.
        // Partial filter keeps soft-deleted rows out of the uniqueness constraint.
        builder.HasIndex(p => new { p.TenantId, p.UserId, p.Category })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_notification_preference_tenant_user_category");
    }
}
