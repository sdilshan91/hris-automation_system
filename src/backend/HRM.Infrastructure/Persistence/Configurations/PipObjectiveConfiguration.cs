using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="PipObjective"/> (US-PRF-008 AC-1/FR-1). Maps to the "pip_objective"
/// table (singular snake_case). Tenant isolation is via the global query filter in AppDbContext +
/// TenantInterceptor.
/// </summary>
public sealed class PipObjectiveConfiguration : IEntityTypeConfiguration<PipObjective>
{
    public void Configure(EntityTypeBuilder<PipObjective> builder)
    {
        builder.ToTable("pip_objective");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");

        builder.Property(o => o.PipId).IsRequired();

        builder.Property(o => o.Title).HasMaxLength(200).IsRequired();
        builder.Property(o => o.Description).HasMaxLength(2000);
        builder.Property(o => o.SuccessCriteria).HasMaxLength(2000).IsRequired();
        builder.Property(o => o.DueDate).IsRequired();
        builder.Property(o => o.SortOrder).IsRequired();
        builder.Property(o => o.AddedAtExtension).HasDefaultValue(false).IsRequired();

        builder.Property(o => o.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(o => new { o.TenantId, o.PipId });
    }
}
