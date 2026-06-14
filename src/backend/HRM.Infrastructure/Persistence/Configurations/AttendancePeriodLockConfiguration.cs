using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the canonical AttendancePeriodLock entity (US-ATT-009 §7, FR-3/FR-4).
/// Maps to the "attendance_period_lock" table — the single attendance-period lock concept that
/// replaces the former US-ATT-003 placeholder PayrollLockPeriod. Tenant-scoped via the global query
/// filter in AppDbContext.OnModelCreating.
/// </summary>
public sealed class AttendancePeriodLockConfiguration : IEntityTypeConfiguration<AttendancePeriodLock>
{
    public void Configure(EntityTypeBuilder<AttendancePeriodLock> builder)
    {
        builder.ToTable("attendance_period_lock");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.Property(p => p.PeriodStart)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.PeriodEnd)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.IsLocked)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // NFR-2 (atomic, DB-enforced): at most one ACTIVE lock per tenant per exact period range.
        // Partial unique index — only currently-locked, non-deleted rows participate, so a period can
        // be locked → unlocked → re-locked (AC-5) without colliding with the historical unlocked row.
        builder.HasIndex(p => new { p.TenantId, p.PeriodStart, p.PeriodEnd })
            .HasDatabaseName("ix_attendance_period_lock_active_unique")
            .IsUnique()
            .HasFilter("is_locked = true AND is_deleted = false");

        // Range lookup for the clock/regularization "is this date locked?" checks.
        builder.HasIndex(p => new { p.TenantId, p.IsLocked, p.PeriodStart, p.PeriodEnd })
            .HasDatabaseName("ix_attendance_period_lock_tenant_range");
    }
}
