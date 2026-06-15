using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the Offer entity (US-REC-007). Maps to the "offer" table with snake_case
/// naming. Tenant isolation is via the global query filter in AppDbContext + TenantInterceptor (this
/// codebase does not use Postgres RLS — same as every other module). Mirrors ApplicantConfiguration.
/// </summary>
public sealed class OfferConfiguration : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> builder)
    {
        builder.ToTable("offer");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.ApplicantId).IsRequired();
        builder.Property(o => o.VacancyId).IsRequired();

        builder.Property(o => o.OfferReferenceNumber)
            .HasMaxLength(30)
            .IsRequired();

        // Status/SalaryFrequency stored as strings for readability/forward-compat (the codebase's
        // varchar-enum pattern, e.g. ApplicantConfiguration).
        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.OfferedPosition)
            .HasMaxLength(200)
            .IsRequired();

        // Cross-module FKs as plain nullable UUID columns (no hard constraint — mirrors the codebase
        // pattern for cross-module references; tenant-scoped existence is validated in the service).
        builder.Property(o => o.DepartmentId);
        builder.Property(o => o.ReportingManagerEmployeeId);

        builder.Property(o => o.SalaryAmount)
            .HasColumnType("numeric(18,2)")
            .IsRequired();

        builder.Property(o => o.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(o => o.SalaryFrequency)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.BenefitsSummary).HasColumnType("text");
        builder.Property(o => o.CustomClauses).HasColumnType("text");

        builder.Property(o => o.StartDate).IsRequired();
        builder.Property(o => o.ExpiryDate).IsRequired();

        builder.Property(o => o.ProbationMonths);

        builder.Property(o => o.PdfStorageKey)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(o => o.Version).HasDefaultValue(1).IsRequired();

        builder.Property(o => o.SentAt);
        builder.Property(o => o.RespondedAt);
        builder.Property(o => o.Response).HasMaxLength(20);
        builder.Property(o => o.ReminderJobId).HasMaxLength(100);

        builder.Property(o => o.IsDeleted).HasDefaultValue(false).IsRequired();

        // §7 reference number is unique per tenant (partial — exclude soft-deleted).
        builder.HasIndex(o => new { o.TenantId, o.OfferReferenceNumber })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // Index on (tenant_id, applicant_id) for the per-applicant offer list + the one-active-offer
        // lookup (BR-2).
        builder.HasIndex(o => new { o.TenantId, o.ApplicantId });
    }
}
