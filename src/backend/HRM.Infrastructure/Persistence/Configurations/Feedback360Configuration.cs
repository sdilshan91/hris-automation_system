using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="Feedback360"/> entity (US-PRF-005). Maps to the "feedback_360"
/// table (singular snake_case). Tenant isolation is via the global query filter in AppDbContext +
/// TenantInterceptor. BR-3 (one feedback per reviewer per reviewee per cycle) is enforced by the tenant-scoped,
/// soft-delete-aware unique index. Optimistic concurrency rides the Npgsql xmin row-version convention.
/// </summary>
public sealed class Feedback360Configuration : IEntityTypeConfiguration<Feedback360>
{
    public void Configure(EntityTypeBuilder<Feedback360> builder)
    {
        builder.ToTable("feedback_360");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id");

        builder.Property(f => f.CycleId).IsRequired();
        builder.Property(f => f.RevieweeEmployeeId).IsRequired();
        builder.Property(f => f.ReviewerEmployeeId).IsRequired();

        builder.Property(f => f.Category)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(f => f.IsAnonymous).HasDefaultValue(false).IsRequired();

        builder.Property(f => f.OverallComment).HasMaxLength(5000);

        builder.Property(f => f.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.Property(f => f.Version).IsRowVersion();

        // BR-3: exactly one feedback per reviewer per reviewee per cycle. Tenant-scoped, soft-delete-aware.
        builder.HasIndex(f => new { f.TenantId, f.CycleId, f.RevieweeEmployeeId, f.ReviewerEmployeeId })
            .IsUnique()
            .HasFilter("is_deleted = false");

        builder.HasIndex(f => new { f.TenantId, f.CycleId, f.RevieweeEmployeeId });

        builder.HasMany(f => f.Items)
            .WithOne(i => i.Feedback360!)
            .HasForeignKey(i => i.Feedback360Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
