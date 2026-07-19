using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for EmployeeWorkHistory (US-CHR-002 / DF-39).
/// FromDate/ToDate are DateOnly → PostgreSQL <c>date</c> columns.
/// </summary>
public sealed class EmployeeWorkHistoryConfiguration : IEntityTypeConfiguration<EmployeeWorkHistory>
{
    public void Configure(EntityTypeBuilder<EmployeeWorkHistory> builder)
    {
        builder.ToTable("employee_work_history");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Company)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Position)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.FromDate)
            .HasColumnType("date");

        builder.Property(e => e.ToDate)
            .HasColumnType("date");

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // Index for efficient queries by employee
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId })
            .HasDatabaseName("ix_employee_work_history_tenant_employee");
    }
}
