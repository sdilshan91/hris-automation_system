using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="ManagerReviewItem"/> entity (US-PRF-003). Maps to the
/// "manager_review_item" table (singular snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor.
/// </summary>
public sealed class ManagerReviewItemConfiguration : IEntityTypeConfiguration<ManagerReviewItem>
{
    public void Configure(EntityTypeBuilder<ManagerReviewItem> builder)
    {
        builder.ToTable("manager_review_item");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.ManagerReviewId).IsRequired();
        builder.Property(i => i.GoalId).IsRequired();

        builder.Property(i => i.ManagerRating);

        // FR-3: manager comment ≥20 chars at submit (length floor enforced in the service). 5000 ceiling here.
        builder.Property(i => i.ManagerComment).HasMaxLength(5000);

        builder.Property(i => i.IsDeleted).HasDefaultValue(false).IsRequired();

        // One item per goal within a manager review. Tenant-scoped, soft-delete-aware.
        builder.HasIndex(i => new { i.TenantId, i.ManagerReviewId, i.GoalId })
            .IsUnique()
            .HasFilter("is_deleted = false");
    }
}
