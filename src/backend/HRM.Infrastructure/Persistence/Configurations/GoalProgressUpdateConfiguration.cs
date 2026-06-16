using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="GoalProgressUpdate"/> (US-PRF-009 FR-1/FR-2/FR-3/NFR-3). Maps to the
/// "goal_progress_update" table (singular snake_case), an APPEND-ONLY history — immutability (NFR-3) is enforced at
/// the service layer (no update/delete path) and there is no concurrency token. Tenant isolation is via the global
/// query filter in AppDbContext + TenantInterceptor (NFR-2). The owned attachment metadata lives in the child
/// <see cref="GoalProgressAttachment"/> rows (FR-2).
/// </summary>
public sealed class GoalProgressUpdateConfiguration : IEntityTypeConfiguration<GoalProgressUpdate>
{
    public void Configure(EntityTypeBuilder<GoalProgressUpdate> builder)
    {
        builder.ToTable("goal_progress_update");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");

        builder.Property(u => u.GoalId).IsRequired();
        builder.Property(u => u.EmployeeId).IsRequired();

        builder.Property(u => u.ProgressPct).IsRequired();

        builder.Property(u => u.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(u => u.Notes).HasMaxLength(2000);
        builder.Property(u => u.CreatedAtUtc).IsRequired();

        builder.Property(u => u.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasMany(u => u.Attachments)
            .WithOne(a => a.ProgressUpdate!)
            .HasForeignKey(a => a.ProgressUpdateId)
            .OnDelete(DeleteBehavior.Cascade);

        // Timeline + "latest update" lookups are by (tenant, goal, created-at).
        builder.HasIndex(u => new { u.TenantId, u.GoalId, u.CreatedAtUtc });

        // FK to Goal / Employee stored as UUID columns without hard FK constraints, mirroring the codebase
        // cross-module reference pattern (tenant-scoped existence validated in the service).
    }
}
