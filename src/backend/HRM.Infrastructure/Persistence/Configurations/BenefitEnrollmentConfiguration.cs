using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="BenefitEnrollment"/> (US-TRN-003 FR-2). Maps to "benefit_enrollments"
/// (snake_case). Enum fields are stored as their string names. A <b>partial unique index</b> on
/// (tenant, plan, employee) filtered to live Active rows enforces "one active enrollment per (plan, employee)"
/// (BR-3, NFR-3) at the database level — a duplicate insert races to a 23505 → 409 already_enrolled.
/// </summary>
public sealed class BenefitEnrollmentConfiguration : IEntityTypeConfiguration<BenefitEnrollment>
{
    public void Configure(EntityTypeBuilder<BenefitEnrollment> builder)
    {
        builder.ToTable("benefit_enrollments");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.BenefitPlanId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.CoverageLevel)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.EffectiveDate).IsRequired();
        builder.Property(e => e.EndDate);
        builder.Property(e => e.ElectedAt).IsRequired();
        builder.Property(e => e.ElectedBy).IsRequired();

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasOne<BenefitPlan>()
            .WithMany()
            .HasForeignKey(e => e.BenefitPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // BR-3/NFR-3: at most one live Active enrollment per (tenant, plan, employee).
        builder.HasIndex(e => new { e.TenantId, e.BenefitPlanId, e.EmployeeId })
            .HasDatabaseName("ux_benefit_enrollments_active")
            .IsUnique()
            .HasFilter("is_deleted = false AND status = 'Active'");

        // AC-8: an employee's enrollments are listed by (tenant, employee, status).
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.Status })
            .HasDatabaseName("ix_benefit_enrollments_tenant_id_employee_id_status");
    }
}
