using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="BenefitEligibilityRule"/> (US-TRN-003 FR-1). Maps to
/// "benefit_eligibility_rules" (snake_case). The <c>Attribute</c> enum is stored as its string name to keep the
/// contract stable; the FK points at "benefit_plans".
/// </summary>
public sealed class BenefitEligibilityRuleConfiguration : IEntityTypeConfiguration<BenefitEligibilityRule>
{
    public void Configure(EntityTypeBuilder<BenefitEligibilityRule> builder)
    {
        builder.ToTable("benefit_eligibility_rules");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.BenefitPlanId).IsRequired();

        builder.Property(r => r.Attribute)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Operator)
            .HasMaxLength(4)
            .IsRequired();

        builder.Property(r => r.Value)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(r => r.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasOne<BenefitPlan>()
            .WithMany()
            .HasForeignKey(r => r.BenefitPlanId)
            .OnDelete(DeleteBehavior.Cascade);

        // Rules are looked up per plan within a tenant.
        builder.HasIndex(r => new { r.TenantId, r.BenefitPlanId })
            .HasDatabaseName("ix_benefit_eligibility_rules_tenant_id_benefit_plan_id");
    }
}
