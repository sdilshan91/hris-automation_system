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

        builder.Property(c => c.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.Status });
    }
}
