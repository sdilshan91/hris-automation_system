using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the MINIMAL <see cref="AppraisalCycle"/> entity (US-PRF-001 unblocking;
/// fleshed out by US-PRF-004). Maps to the "appraisal_cycle" table with snake_case naming. Tenant
/// isolation is via the global query filter in AppDbContext + TenantInterceptor (no Postgres RLS —
/// same as every other module).
/// </summary>
public sealed class AppraisalCycleConfiguration : IEntityTypeConfiguration<AppraisalCycle>
{
    public void Configure(EntityTypeBuilder<AppraisalCycle> builder)
    {
        builder.ToTable("appraisal_cycle");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.GoalSettingStart).IsRequired();
        builder.Property(c => c.GoalSettingEnd).IsRequired();

        // US-PRF-002: self-assessment window + scoring config. Defaults make existing rows valid:
        // a 1-5 scale (RatingScaleMax=5) and a 30:70 self:manager ratio (SelfWeightPercent=30). The window
        // columns default to the goal-setting window for existing rows via the migration default (epoch),
        // but new cycles set them explicitly.
        builder.Property(c => c.SelfAssessmentStart).IsRequired();
        builder.Property(c => c.SelfAssessmentEnd).IsRequired();
        builder.Property(c => c.RatingScaleMax).HasDefaultValue(5).IsRequired();
        builder.Property(c => c.SelfWeightPercent).HasDefaultValue(30).IsRequired();

        builder.Property(c => c.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.Status });
    }
}
