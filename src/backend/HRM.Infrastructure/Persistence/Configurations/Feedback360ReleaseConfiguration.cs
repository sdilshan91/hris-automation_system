using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="Feedback360Release"/> entity (US-PRF-005 BR-4/FR-3). Maps to the
/// "feedback_360_release" table (singular snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor. BR-4 (one release per reviewee per cycle) is enforced by the tenant-scoped,
/// soft-delete-aware unique index — the same durable guard the idempotent double-release relies on. The FKs to
/// the cycle and the reviewee use <c>Restrict</c> (matching the PipCheckpoint.ObjectiveId precedent, PR #507) so
/// a release marker can never be silently cascade-deleted out from under a released reviewee.
/// </summary>
public sealed class Feedback360ReleaseConfiguration : IEntityTypeConfiguration<Feedback360Release>
{
    public void Configure(EntityTypeBuilder<Feedback360Release> builder)
    {
        builder.ToTable("feedback_360_release");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");

        builder.Property(r => r.CycleId).IsRequired();
        builder.Property(r => r.RevieweeEmployeeId).IsRequired();
        builder.Property(r => r.ReleasedAt).IsRequired();
        builder.Property(r => r.ReleasedByEmployeeId).IsRequired();

        builder.Property(r => r.IsDeleted).HasDefaultValue(false).IsRequired();

        // BR-4: exactly one release per reviewee per cycle. Tenant-scoped, soft-delete-aware — the real guard
        // behind the idempotent re-release (a retried click resolves to the existing row, never a second one).
        builder.HasIndex(r => new { r.TenantId, r.CycleId, r.RevieweeEmployeeId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasOne(r => r.Cycle)
            .WithMany()
            .HasForeignKey(r => r.CycleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Reviewee)
            .WithMany()
            .HasForeignKey(r => r.RevieweeEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
