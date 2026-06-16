using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="SelfAssessment"/> entity (US-PRF-002). Maps to the
/// "self_assessment" table (singular snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor (no Postgres RLS — same as every other module). Optimistic
/// concurrency rides the Npgsql xmin row-version convention.
/// </summary>
public sealed class SelfAssessmentConfiguration : IEntityTypeConfiguration<SelfAssessment>
{
    public void Configure(EntityTypeBuilder<SelfAssessment> builder)
    {
        builder.ToTable("self_assessment");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.CycleId).IsRequired();
        builder.Property(s => s.EmployeeId).IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.WeightedSelfScore)
            .HasColumnType("numeric(6,2)");

        builder.Property(s => s.IsDeleted).HasDefaultValue(false).IsRequired();

        // NFR optimistic concurrency: maps to the PostgreSQL xmin system column (no schema DDL). Mirrors
        // Goal.Version / LeaveRequest.Version. The InMemory test provider ignores this annotation.
        builder.Property(s => s.Version).IsRowVersion();

        // Exactly one self-assessment per employee per cycle (US-PRF-002 §7). Tenant-scoped, soft-delete-aware.
        builder.HasIndex(s => new { s.TenantId, s.CycleId, s.EmployeeId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasMany(s => s.Items)
            .WithOne(i => i.SelfAssessment!)
            .HasForeignKey(i => i.SelfAssessmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
