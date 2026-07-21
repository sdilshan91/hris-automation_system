using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the ApplicantPortalToken entity (US-REC-008). Maps to the
/// "applicant_portal_token" table with snake_case naming. Tenant isolation is via the global query filter
/// in AppDbContext + TenantInterceptor (this codebase does not use Postgres RLS — same as every other
/// module). Mirrors OfferConfiguration / ApplicantConfiguration.
/// </summary>
public sealed class ApplicantPortalTokenConfiguration : IEntityTypeConfiguration<ApplicantPortalToken>
{
    public void Configure(EntityTypeBuilder<ApplicantPortalToken> builder)
    {
        builder.ToTable("applicant_portal_token");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.ApplicantEmail)
            .HasMaxLength(256)
            .IsRequired();

        // SHA-256 hex digest = 64 chars.
        builder.Property(t => t.TokenHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(t => t.ExpiresAt).IsRequired();

        builder.Property(t => t.IsDeleted).HasDefaultValue(false).IsRequired();

        // FR-1/BR-6: look up active tokens by tenant + applicant email (case-insensitive lookup is done
        // against the lower-cased stored value).
        builder.HasIndex(t => new { t.TenantId, t.ApplicantEmail });

        // DF-59: the DbCountPortalLinkIpRateLimiter fallback (used only when Redis is NOT configured) counts
        // tokens filtered by (tenant_id, request_ip, created_at). Without this composite index that per-issue
        // COUNT seq-scans applicant_portal_token at scale. Equality on (tenant_id, request_ip) + range on
        // created_at. The Redis path avoids the COUNT entirely, so this only matters when Redis is off.
        builder.HasIndex(t => new { t.TenantId, t.RequestIp, t.CreatedAt });
    }
}
