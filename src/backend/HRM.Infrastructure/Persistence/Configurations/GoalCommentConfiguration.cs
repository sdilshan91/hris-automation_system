using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="GoalComment"/> (US-PRF-009 FR-8). Maps to the "goal_comment" table
/// (singular snake_case), an append-only manager/HR comment thread per goal. Tenant isolation is via the global
/// query filter in AppDbContext + TenantInterceptor (NFR-2).
/// </summary>
public sealed class GoalCommentConfiguration : IEntityTypeConfiguration<GoalComment>
{
    public void Configure(EntityTypeBuilder<GoalComment> builder)
    {
        builder.ToTable("goal_comment");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.GoalId).IsRequired();
        builder.Property(c => c.ProgressUpdateId);
        builder.Property(c => c.AuthorEmployeeId).IsRequired();
        builder.Property(c => c.AuthorName).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Body).HasMaxLength(500).IsRequired();
        builder.Property(c => c.CreatedAtUtc).IsRequired();

        builder.Property(c => c.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.GoalId, c.CreatedAtUtc });
    }
}
