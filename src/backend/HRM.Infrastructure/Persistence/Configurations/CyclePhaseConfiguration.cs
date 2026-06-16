using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="CyclePhase"/> (US-PRF-004). Maps to the "cycle_phase" table with
/// snake_case naming. Tenant isolation is via the global query filter in AppDbContext + TenantInterceptor.
/// A cycle has at most one phase of each type (enforced by a tenant-scoped unique index).
/// </summary>
public sealed class CyclePhaseConfiguration : IEntityTypeConfiguration<CyclePhase>
{
    public void Configure(EntityTypeBuilder<CyclePhase> builder)
    {
        builder.ToTable("cycle_phase");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.CycleId).IsRequired();

        builder.Property(p => p.PhaseType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.Sequence).IsRequired();
        builder.Property(p => p.StartDate).IsRequired();
        builder.Property(p => p.EndDate).IsRequired();

        builder.Property(p => p.IsDeleted).HasDefaultValue(false).IsRequired();

        // One phase of each type per cycle (soft-delete aware).
        builder.HasIndex(p => new { p.TenantId, p.CycleId, p.PhaseType })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(p => new { p.TenantId, p.CycleId });
    }
}
