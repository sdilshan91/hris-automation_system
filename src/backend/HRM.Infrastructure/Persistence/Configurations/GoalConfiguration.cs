using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="Goal"/> entity (US-PRF-001). Maps to the "goal" table with
/// snake_case naming. Tenant isolation is via the global query filter in AppDbContext + TenantInterceptor
/// (no Postgres RLS — same as every other module). Optimistic concurrency (NFR-4) uses the Npgsql xmin
/// row-version convention.
/// </summary>
public sealed class GoalConfiguration : IEntityTypeConfiguration<Goal>
{
    public void Configure(EntityTypeBuilder<Goal> builder)
    {
        builder.ToTable("goal");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id");

        builder.Property(g => g.CycleId).IsRequired();
        builder.Property(g => g.EmployeeId).IsRequired();

        builder.Property(g => g.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(g => g.Description)
            .HasMaxLength(2000);

        builder.Property(g => g.Category)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(g => g.Weight).IsRequired();

        builder.Property(g => g.TargetValue)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(g => g.MeasurementUnit)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(g => g.DueDate).IsRequired();

        builder.Property(g => g.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(g => g.IsDeleted).HasDefaultValue(false).IsRequired();

        // NFR-4 optimistic concurrency: a uint marked IsRowVersion() is mapped to the PostgreSQL xmin
        // system column by Npgsql's convention (NO schema DDL — xmin exists on every table). We must NOT
        // override the column name/type, or a spurious ADD COLUMN xmin is scaffolded. The InMemory test
        // provider ignores this annotation. Mirrors LeaveRequest.Version.
        builder.Property(g => g.Version).IsRowVersion();

        // Indexes for the per-employee-per-cycle weight/count rollups + dashboard reads.
        builder.HasIndex(g => new { g.TenantId, g.CycleId, g.EmployeeId });

        // FKs are stored as UUID columns without hard FK constraints, mirroring the codebase pattern for
        // cross-module references (tenant-scoped existence is validated in the service). The self-FK
        // (ParentGoalId) likewise has no DB constraint to avoid cascade/soft-delete complications.
    }
}
