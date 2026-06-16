using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="ReviewerAssignment"/> entity (US-PRF-005). Maps to the
/// "reviewer_assignment" table (singular snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor (no Postgres RLS — same as every other module). The tenant-scoped,
/// soft-delete-aware unique index enforces one assignment per reviewer per reviewee per category per cycle.
/// </summary>
public sealed class ReviewerAssignmentConfiguration : IEntityTypeConfiguration<ReviewerAssignment>
{
    public void Configure(EntityTypeBuilder<ReviewerAssignment> builder)
    {
        builder.ToTable("reviewer_assignment");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.CycleId).IsRequired();
        builder.Property(a => a.RevieweeEmployeeId).IsRequired();
        builder.Property(a => a.ReviewerEmployeeId).IsRequired();

        builder.Property(a => a.Category)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();

        // NFR-3 optimistic concurrency: maps to the PostgreSQL xmin system column (no schema DDL). Mirrors
        // ManagerReview.Version. The InMemory test provider ignores this annotation.
        builder.Property(a => a.Version).IsRowVersion();

        // One reviewer per reviewee per category per cycle (US-PRF-005). Including Category lets the same
        // person appear once as Self and once as Manager only if those categories differ. Tenant-scoped,
        // soft-delete-aware.
        builder.HasIndex(a => new { a.TenantId, a.CycleId, a.RevieweeEmployeeId, a.ReviewerEmployeeId, a.Category })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(a => new { a.TenantId, a.CycleId, a.RevieweeEmployeeId });
    }
}
