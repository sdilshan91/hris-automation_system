using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for EmploymentHistory (US-CHR-002 FR-6).
/// Append-only timeline of employment changes.
/// </summary>
public sealed class EmploymentHistoryConfiguration : IEntityTypeConfiguration<EmploymentHistory>
{
    public void Configure(EntityTypeBuilder<EmploymentHistory> builder)
    {
        builder.ToTable("employment_histories");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.ChangeType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.PreviousValue)
            .HasMaxLength(500);

        builder.Property(e => e.NewValue)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.EffectiveDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(e => e.Reason)
            .HasMaxLength(1000);

        builder.Property(e => e.ChangedBy)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // Indexes for efficient queries
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.EffectiveDate })
            .HasDatabaseName("ix_employment_histories_tenant_employee_effective_date");

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.ChangeType })
            .HasDatabaseName("ix_employment_histories_tenant_employee_change_type");
    }
}
