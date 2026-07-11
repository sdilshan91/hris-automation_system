using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="BenefitPlan"/> entity (US-TRN-002).
/// Maps to the "benefit_plans" table with snake_case naming convention.
/// </summary>
public sealed class BenefitPlanConfiguration : IEntityTypeConfiguration<BenefitPlan>
{
    public void Configure(EntityTypeBuilder<BenefitPlan> builder)
    {
        builder.ToTable("benefit_plans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.Property(p => p.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(p => p.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Description)
            .HasMaxLength(2000);

        builder.Property(p => p.CoverageDetails)
            .HasMaxLength(4000);

        builder.Property(p => p.EmployerCost)
            .HasColumnType("numeric(14,2)");

        builder.Property(p => p.EmployeeCost)
            .HasColumnType("numeric(14,2)");

        builder.Property(p => p.Currency)
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(p => p.EffectiveFrom).IsRequired();
        builder.Property(p => p.EffectiveTo);
        builder.Property(p => p.EnrollmentOpensAt);
        builder.Property(p => p.EnrollmentClosesAt);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // NFR-1/AC-5: list queries filter by (tenant, status).
        builder.HasIndex(p => new { p.TenantId, p.Status })
            .HasDatabaseName("ix_benefit_plans_tenant_id_status");
    }
}
