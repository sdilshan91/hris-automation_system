using System.Text.Json;
using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", t =>
        {
            t.HasCheckConstraint(
                "ck_tenants_subdomain_format",
                "subdomain = lower(subdomain) AND length(subdomain) BETWEEN 3 AND 63 AND subdomain ~ '^[a-z0-9]([a-z0-9-]*[a-z0-9])$'");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id");

        builder.Property(t => t.Subdomain)
            .HasMaxLength(63)
            .IsRequired();

        builder.HasIndex(t => t.Subdomain)
            .IsUnique();

        builder.HasIndex(t => new { t.Subdomain, t.Status })
            .HasDatabaseName("ix_tenants_subdomain_status")
            .HasFilter("is_deleted = false");

        // ISSUE-304 (US-CHR-009 BR-6): the tenant probation period. Default 90 = the value
        // EmployeeStatusService used to hardcode, so an unconfigured tenant is unchanged.
        builder.Property(t => t.ProbationPeriodDays)
            .HasDefaultValue(90)
            .IsRequired();

        builder.Property(t => t.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Status)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(t => t.PlanId)
            .HasMaxLength(64)
            .HasDefaultValue("default")
            .IsRequired();

        builder.Property(t => t.EnabledModules)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (c1, c2) => (c1 ?? new()).SequenceEqual(c2 ?? new()),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));

        builder.Property(t => t.LogoUrl)
            .HasMaxLength(500);

        builder.Property(t => t.PrimaryColor)
            .HasMaxLength(20);

        builder.Property(t => t.ContactEmail)
            .HasMaxLength(150);

        // Multi-country tax foundation: default/fallback ISO tax country (max 5 to match StatutoryRule.CountryCode).
        builder.Property(t => t.DefaultCountryCode)
            .HasMaxLength(5);

        // US-ADM-001: trial expiry + billing contact (BR-3 / BR-4).
        builder.Property(t => t.TrialEndsAt);

        builder.Property(t => t.BillingEmail)
            .HasMaxLength(150);

        // US-ADM-004: lifecycle suspend/terminate fields.
        builder.Property(t => t.SuspendedAt);
        builder.Property(t => t.SuspendedReason)
            .HasMaxLength(500);
        builder.Property(t => t.TerminationScheduledAt);

        builder.Property(t => t.MfaPolicy)
            .HasMaxLength(20)
            .HasDefaultValue("off");

        builder.Property(t => t.ConcurrentSessionStrategy)
            .HasMaxLength(20)
            .HasDefaultValue("revoke_oldest");

        // US-ADM-008 FR-6/BR-5: plan-governed audit-log retention window. DB default 90 (Starter) so existing
        // tenant rows backfill to a sane window rather than 0 (which the purge service treats as 90 anyway).
        builder.Property(t => t.AuditLogRetentionDays)
            .HasDefaultValue(90);

        // BUG-244 (US-PRF-005): default true so existing tenant rows opt IN to manager reviewer-config on
        // migrate (managers-allowed is the intended default). New tenants also default true via the entity.
        builder.Property(t => t.AllowManagerReviewerConfig)
            .HasDefaultValue(true);

        // MfaRequiredRoles stored as jsonb column with value converter and comparer
        builder.Property(t => t.MfaRequiredRoles)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (c1, c2) => (c1 ?? new()).SequenceEqual(c2 ?? new()),
                c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                c => c.ToList()));
    }
}
