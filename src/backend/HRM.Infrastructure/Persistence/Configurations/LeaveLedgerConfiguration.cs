using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the LeaveLedger entity (US-LV-002 FR-5).
/// </summary>
public sealed class LeaveLedgerConfiguration : IEntityTypeConfiguration<LeaveLedger>
{
    public void Configure(EntityTypeBuilder<LeaveLedger> builder)
    {
        builder.ToTable("leave_ledger");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasColumnName("id");

        // BUG-037/086: EntryType is persisted as its enum name. A legacy/seed row stored the Accrual
        // entry as the string "Accrued" — not a LedgerEntryType member — so the default enum<->string
        // conversion threw during materialization and 500'd every 2026 balance/utilization read that
        // touched that row. Tolerate that one legacy alias on READ; writes always emit the canonical
        // enum name. Genuinely unknown values still throw, so real data corruption is not masked.
        builder.Property(l => l.EntryType)
            .HasConversion(
                v => v.ToString(),
                v => ParseEntryType(v))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(l => l.EmployeeId)
            .IsRequired();

        builder.Property(l => l.LeaveTypeId)
            .IsRequired();

        builder.Property(l => l.LeaveYear)
            .IsRequired();

        // US-LV-005 §7: optional FK to the leave request that produced this entry (set on the
        // "Used" deduction written at approval; null for accruals and other entry types).
        builder.Property(l => l.LeaveRequestId);

        builder.Property(l => l.Amount)
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        builder.Property(l => l.BalanceAfter)
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        builder.Property(l => l.Description)
            .HasMaxLength(500);

        builder.Property(l => l.OccurredAt)
            .IsRequired();

        builder.Property(l => l.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Relationships ──────────────────────────────────────────

        builder.HasOne(l => l.Employee)
            .WithMany()
            .HasForeignKey(l => l.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.LeaveType)
            .WithMany()
            .HasForeignKey(l => l.LeaveTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // US-LV-005 §7: optional FK to the originating leave request. SET NULL so deleting a
        // request (rare; requests are soft-deleted) does not orphan the immutable ledger entry.
        builder.HasOne(l => l.LeaveRequest)
            .WithMany()
            .HasForeignKey(l => l.LeaveRequestId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Indexes ────────────────────────────────────────────────

        // Primary lookup: ledger entries for an employee + leave type + year.
        builder.HasIndex(l => new { l.TenantId, l.EmployeeId, l.LeaveTypeId, l.LeaveYear })
            .HasDatabaseName("ix_leave_ledger_tenant_emp_type_year");

        // Chronological listing by occurrence.
        builder.HasIndex(l => new { l.TenantId, l.OccurredAt })
            .HasDatabaseName("ix_leave_ledger_tenant_occurred_at");
    }

    /// <summary>
    /// Reads a persisted EntryType string back to the enum, tolerating the legacy "Accrued" alias for
    /// <see cref="LedgerEntryType.Accrual"/> (BUG-037/086). Unknown values still throw.
    /// </summary>
    private static LedgerEntryType ParseEntryType(string value) =>
        value == "Accrued"
            ? LedgerEntryType.Accrual
            : Enum.Parse<LedgerEntryType>(value);
}
