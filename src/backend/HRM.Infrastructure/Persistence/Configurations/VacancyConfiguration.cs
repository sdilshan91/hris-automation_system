using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the Vacancy entity (US-REC-001). Maps to the "vacancy" table with
/// snake_case naming. Tenant isolation is via the global query filter in AppDbContext +
/// TenantInterceptor (this codebase does not use Postgres RLS — same as every other module).
/// </summary>
public sealed class VacancyConfiguration : IEntityTypeConfiguration<Vacancy>
{
    public void Configure(EntityTypeBuilder<Vacancy> builder)
    {
        builder.ToTable("vacancy");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id");

        builder.Property(v => v.ReferenceNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(v => v.Title)
            .HasMaxLength(200)
            .IsRequired();

        // Status stored as string for readability/forward-compat (matches the codebase's varchar status pattern).
        builder.Property(v => v.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(v => v.EmploymentType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(v => v.Headcount).IsRequired();
        builder.Property(v => v.FilledCount).HasDefaultValue(0).IsRequired();

        builder.Property(v => v.SalaryMin).HasColumnType("numeric(14,2)");
        builder.Property(v => v.SalaryMax).HasColumnType("numeric(14,2)");
        builder.Property(v => v.SalaryCurrency).HasMaxLength(3);

        // Sanitized HTML — stored as text (NFR-4 sanitization happens in the service before persist).
        builder.Property(v => v.Description).HasColumnType("text").IsRequired();
        builder.Property(v => v.Qualifications).HasColumnType("text");

        builder.Property(v => v.Slug).HasMaxLength(120);

        builder.Property(v => v.PublishToPublicCareers).HasDefaultValue(true).IsRequired();

        builder.Property(v => v.IsDeleted).HasDefaultValue(false).IsRequired();

        // FKs are stored as nullable UUID columns. Hard FK constraints are intentionally omitted to
        // mirror the codebase pattern for cross-module references (e.g. JobTitle.GradeId) and to keep
        // soft-deleted parents from blocking deletes; tenant-scoped existence is validated in the service.
        builder.Property(v => v.DepartmentId);
        builder.Property(v => v.JobTitleId);
        builder.Property(v => v.LocationId);
        builder.Property(v => v.HiringManagerId);

        builder.Ignore(v => v.Department);
        builder.Ignore(v => v.JobTitle);
        builder.Ignore(v => v.Location);
        builder.Ignore(v => v.HiringManager);

        // §7/§10: reference number is unique per tenant (partial — exclude soft-deleted).
        builder.HasIndex(v => new { v.TenantId, v.ReferenceNumber })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // FR-5: slug is unique per tenant (for public careers URLs). Partial: only non-null + not soft-deleted.
        builder.HasIndex(v => new { v.TenantId, v.Slug })
            .IsUnique()
            .HasFilter("slug IS NOT NULL AND is_deleted = false");

        // §7 Storage: indexes on (tenant_id, status) and (tenant_id, department_id) for the list view (NFR-1).
        builder.HasIndex(v => new { v.TenantId, v.Status });
        builder.HasIndex(v => new { v.TenantId, v.DepartmentId });
    }
}
