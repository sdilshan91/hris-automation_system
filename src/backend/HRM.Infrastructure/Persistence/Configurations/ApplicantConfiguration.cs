using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the Applicant entity (US-REC-002). Maps to the "applicant" table with
/// snake_case naming. Tenant isolation is via the global query filter in AppDbContext +
/// TenantInterceptor (this codebase does not use Postgres RLS — same as every other module).
/// </summary>
public sealed class ApplicantConfiguration : IEntityTypeConfiguration<Applicant>
{
    public void Configure(EntityTypeBuilder<Applicant> builder)
    {
        builder.ToTable("applicant");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.VacancyId).IsRequired();

        builder.Property(a => a.ApplicationReferenceNumber)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(a => a.FirstName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.LastName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(a => a.Phone).HasMaxLength(40);

        // Cover letter is plain text (BR-1 max 2000 enforced by the validator).
        builder.Property(a => a.CoverLetter).HasColumnType("text");

        builder.Property(a => a.ResumeStorageKey)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(a => a.ResumeFileName)
            .HasMaxLength(255)
            .IsRequired();

        // Stage/Source stored as strings for readability/forward-compat (matches the codebase's varchar
        // status pattern, e.g. VacancyConfiguration).
        builder.Property(a => a.Stage)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.Source)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // US-REC-004 AC-4/FR-3: structured rejection reason for the applicant's current state. Nullable —
        // set on move to Rejected, cleared on reactivation (BR-2). Stored as a string (varchar enum pattern).
        builder.Property(a => a.RejectionReason)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(a => a.IsInternal).HasDefaultValue(false).IsRequired();

        // FK stored as a nullable UUID column. A hard FK constraint is intentionally omitted to mirror
        // the codebase pattern for cross-module references (e.g. Vacancy.HiringManagerId) and to keep a
        // soft-deleted parent from blocking; tenant-scoped existence is validated in the service.
        builder.Property(a => a.LinkedEmployeeId);

        builder.Property(a => a.AppliedAt).IsRequired();

        // US-REC-010 FR-6: conversion linkage. ConvertedToEmployeeId is a soft cross-module reference
        // (no hard FK, mirroring LinkedEmployeeId); a partial unique index guards against two applicants
        // pointing at the same employee record within a tenant.
        builder.Property(a => a.ConvertedToEmployeeId);
        builder.Property(a => a.ConvertedAt);
        builder.Property(a => a.ConvertedByUserId);

        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();

        // §7 reference number is unique per tenant (partial — exclude soft-deleted).
        builder.HasIndex(a => new { a.TenantId, a.ApplicationReferenceNumber })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // §7 Storage: index on (tenant_id, vacancy_id) for the recruiter list view.
        builder.HasIndex(a => new { a.TenantId, a.VacancyId });

        // BR-1/AC-3: an applicant is unique per vacancy by email (duplicate detection). Partial —
        // exclude soft-deleted so a withdrawn application doesn't permanently block re-applying.
        builder.HasIndex(a => new { a.TenantId, a.VacancyId, a.Email })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // US-REC-010 FR-10/BR-2: at most one applicant per tenant may point at a given employee record.
        // Partial — only rows that have actually been converted (employee id not null).
        builder.HasIndex(a => new { a.TenantId, a.ConvertedToEmployeeId })
            .IsUnique()
            .HasFilter("converted_to_employee_id IS NOT NULL");
    }
}
