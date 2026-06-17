using System.Text.Json;
using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="SubscriptionPlan"/> (US-ADM-001, extended by US-ADM-009). Platform/system
/// table "subscription_plans" — NOT tenant-scoped (no global query filter applied in AppDbContext).
/// </summary>
public sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("subscription_plans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.Property(p => p.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.Code)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(p => p.Code)
            .IsUnique();

        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        builder.Property(p => p.PriceMonthly)
            .HasColumnType("numeric(12,2)")
            .IsRequired();

        builder.Property(p => p.PriceYearly)
            .HasColumnType("numeric(12,2)")
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(p => p.Currency)
            .HasMaxLength(3)
            .HasDefaultValue("USD")
            .IsRequired();

        builder.Property(p => p.TrialDays)
            .IsRequired();

        builder.Property(p => p.IsPublic)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        // ── Usage limits — nullable (NULL = unlimited, BR-3) ──
        builder.Property(p => p.MaxEmployees);
        builder.Property(p => p.MaxStorageGb);
        builder.Property(p => p.MaxApiCallsPerMonth);
        builder.Property(p => p.MaxEmailSendsPerMonth);
        builder.Property(p => p.MaxCustomRoles);
        builder.Property(p => p.MaxCustomFieldsPerEntity);
        builder.Property(p => p.MaxWorkflows);

        builder.Property(p => p.AuditLogRetentionDays)
            .HasDefaultValue(90)
            .IsRequired();

        builder.Property(p => p.SlaTier)
            .HasMaxLength(50)
            .HasDefaultValue("standard")
            .IsRequired();

        // ── EnabledModules: jsonb array of module keys (US-ADM-009 FR-6) ──
        builder.Property(p => p.EnabledModules)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (c1, c2) => (c1 ?? new()).SequenceEqual(c2 ?? new()),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));

        // ── FeatureFlags: typed flags object serialized to jsonb (US-ADM-009 FR-2) ──
        builder.Property(p => p.FeatureFlags)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'{}'::jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<PlanFeatureFlags>(v, (JsonSerializerOptions?)null) ?? new PlanFeatureFlags())
            .Metadata.SetValueComparer(new ValueComparer<PlanFeatureFlags>(
                (c1, c2) => JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null),
                c => JsonSerializer.Serialize(c, (JsonSerializerOptions?)null).GetHashCode(),
                c => JsonSerializer.Deserialize<PlanFeatureFlags>(JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null) ?? new PlanFeatureFlags()));

        builder.Property(p => p.CreatedAt)
            .IsRequired();

        builder.Property(p => p.UpdatedAt);
    }
}
