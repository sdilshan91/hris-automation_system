using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the EmployeeShift assignment (US-ATT-005 §7, FR-4). Maps to
/// "employee_shift". Effective-dated; tenant-scoped via the global query filter.
/// </summary>
public sealed class EmployeeShiftConfiguration : IEntityTypeConfiguration<EmployeeShift>
{
    public void Configure(EntityTypeBuilder<EmployeeShift> builder)
    {
        builder.ToTable("employee_shift");

        builder.HasKey(es => es.Id);

        builder.Property(es => es.Id)
            .HasColumnName("id");

        builder.Property(es => es.EffectiveFrom)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(es => es.EffectiveTo)
            .HasColumnType("date");

        builder.Property(es => es.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // FR-7 resolution and BR-2 overlap checks query by (employee, effective_from).
        builder.HasIndex(es => new { es.EmployeeId, es.EffectiveFrom })
            .HasDatabaseName("ix_employee_shift_employee_effective");

        builder.HasOne(es => es.Shift)
            .WithMany()
            .HasForeignKey(es => es.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
