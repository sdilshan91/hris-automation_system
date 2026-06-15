using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the SalaryRevisionHistory entity (US-PAY-002 FR-4/BR-3). Maps to the
/// "salary_revision_history" table with snake_case naming. Append-only audit trail; tenant isolation via
/// the global query filter + TenantInterceptor.
/// </summary>
public sealed class SalaryRevisionHistoryConfiguration : IEntityTypeConfiguration<SalaryRevisionHistory>
{
    public void Configure(EntityTypeBuilder<SalaryRevisionHistory> builder)
    {
        builder.ToTable("salary_revision_history");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.EmployeeId).IsRequired();
        builder.Property(x => x.OldStructureId);
        builder.Property(x => x.NewStructureId).IsRequired();

        builder.Property(x => x.OldAnnualCtc).HasColumnType("numeric(18,2)");
        builder.Property(x => x.NewAnnualCtc).HasColumnType("numeric(18,2)").IsRequired();

        builder.Property(x => x.EffectiveFrom).IsRequired();
        builder.Property(x => x.Reason).HasColumnType("text");

        builder.Property(x => x.ChangedBy).IsRequired();
        builder.Property(x => x.ChangedAt).IsRequired();

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // Read path: a single employee's revision timeline, newest first.
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ChangedAt });
    }
}
