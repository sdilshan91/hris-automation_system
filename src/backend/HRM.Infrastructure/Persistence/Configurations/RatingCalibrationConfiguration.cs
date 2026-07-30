using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="RatingCalibration"/> entity (US-PRF-011). Maps to the
/// "rating_calibration" table (singular snake_case). Append-only calibration history; tenant isolation is via
/// the global query filter in AppDbContext + TenantInterceptor + a dormant Postgres RLS policy (creating
/// migration). Scores use numeric(6,2) to match <c>manager_review.final_score</c>.
/// </summary>
public sealed class RatingCalibrationConfiguration : IEntityTypeConfiguration<RatingCalibration>
{
    public void Configure(EntityTypeBuilder<RatingCalibration> builder)
    {
        builder.ToTable("rating_calibration");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.CycleId).IsRequired();
        builder.Property(c => c.EmployeeId).IsRequired();
        builder.Property(c => c.ManagerReviewId).IsRequired();

        builder.Property(c => c.OriginalScore).HasColumnType("numeric(6,2)").IsRequired();
        builder.Property(c => c.PreviousCalibratedScore).HasColumnType("numeric(6,2)");
        builder.Property(c => c.CalibratedScore).HasColumnType("numeric(6,2)").IsRequired();

        builder.Property(c => c.Reason).HasMaxLength(2000).IsRequired();
        builder.Property(c => c.CalibratedByUserId).IsRequired();

        builder.Property(c => c.IsDeleted).HasDefaultValue(false).IsRequired();

        // The calibration cohort + "latest calibration per employee" reads filter by cycle + employee.
        builder.HasIndex(c => new { c.TenantId, c.CycleId, c.EmployeeId });

        builder.HasOne(c => c.Cycle)
            .WithMany()
            .HasForeignKey(c => c.CycleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.ManagerReview)
            .WithMany()
            .HasForeignKey(c => c.ManagerReviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
