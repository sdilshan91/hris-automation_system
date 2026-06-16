using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="CycleParticipant"/> (US-PRF-004 FR-3). Maps to the "cycle_participant"
/// table with snake_case naming. Tenant isolation is via the global query filter in AppDbContext +
/// TenantInterceptor. An employee appears at most once per cycle (tenant-scoped unique index).
/// </summary>
public sealed class CycleParticipantConfiguration : IEntityTypeConfiguration<CycleParticipant>
{
    public void Configure(EntityTypeBuilder<CycleParticipant> builder)
    {
        builder.ToTable("cycle_participant");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.CycleId).IsRequired();
        builder.Property(p => p.EmployeeId).IsRequired();

        builder.Property(p => p.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.CycleId, p.EmployeeId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // BR-4 lookup: an employee's active cycles by type — served via the cycle's (TenantId, Type, Status)
        // index joined on participant rows; this index speeds the participant → cycle resolution.
        builder.HasIndex(p => new { p.TenantId, p.EmployeeId });
    }
}
