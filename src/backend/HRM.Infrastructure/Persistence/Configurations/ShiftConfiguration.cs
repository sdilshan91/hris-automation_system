using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the Shift entity (US-ATT-005 §7). Maps to the "shift" table with
/// snake_case naming. Name is unique per tenant (BR-1) via a soft-delete-aware partial index.
/// </summary>
public sealed class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("shift");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id");

        builder.Property(s => s.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(s => s.Type)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.StartTime)
            .HasColumnType("time");

        builder.Property(s => s.EndTime)
            .HasColumnType("time");

        builder.Property(s => s.BreakDurationMinutes)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.GracePeriodMinutes)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.MinimumHours)
            .HasColumnType("numeric(4,2)");

        builder.Property(s => s.WorkingDays)
            .HasColumnType("integer[]");

        builder.Property(s => s.IsDefault)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(s => s.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(s => s.RotationReferenceStartDate)
            .HasColumnType("date");

        builder.Property(s => s.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // BR-1 / AC-1: shift name unique per tenant; partial index lets a deleted name be reused.
        builder.HasIndex(s => new { s.TenantId, s.Name })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_shift_tenant_name_unique");

        // FR-5: at most one default shift per tenant.
        builder.HasIndex(s => s.TenantId)
            .IsUnique()
            .HasFilter("is_default = true AND is_deleted = false")
            .HasDatabaseName("ix_shift_tenant_default_unique");

        builder.HasMany(s => s.RotationSteps)
            .WithOne(rs => rs.Shift!)
            .HasForeignKey(rs => rs.ShiftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// EF Core configuration for a rotation step (US-ATT-005 FR-7). Maps to "shift_rotation_step".
/// </summary>
public sealed class ShiftRotationStepConfiguration : IEntityTypeConfiguration<ShiftRotationStep>
{
    public void Configure(EntityTypeBuilder<ShiftRotationStep> builder)
    {
        builder.ToTable("shift_rotation_step");

        builder.HasKey(rs => rs.Id);

        builder.Property(rs => rs.Id)
            .HasColumnName("id");

        builder.Property(rs => rs.Order)
            .IsRequired();

        builder.Property(rs => rs.DurationDays)
            .IsRequired();

        builder.Property(rs => rs.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasIndex(rs => new { rs.ShiftId, rs.Order })
            .HasDatabaseName("ix_shift_rotation_step_shift_order");
    }
}
