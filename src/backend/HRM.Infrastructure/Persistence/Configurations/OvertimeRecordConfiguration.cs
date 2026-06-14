using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the OvertimeRecord entity (US-ATT-006 §7).
/// Maps to the "overtime_record" table with snake_case naming convention.
/// </summary>
public sealed class OvertimeRecordConfiguration : IEntityTypeConfiguration<OvertimeRecord>
{
    public void Configure(EntityTypeBuilder<OvertimeRecord> builder)
    {
        builder.ToTable("overtime_record");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id");

        builder.Property(o => o.EmployeeId)
            .IsRequired();

        builder.Property(o => o.AttendanceLogId);

        builder.Property(o => o.Date)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(o => o.OvertimeMinutes)
            .IsRequired();

        builder.Property(o => o.ApprovedMinutes);

        builder.Property(o => o.Multiplier)
            .HasColumnType("numeric(3,2)")
            .IsRequired();

        builder.Property(o => o.Type)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.Reason)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(o => o.ManagerComment)
            .HasColumnType("text");

        builder.Property(o => o.IsPayrollReady)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(o => o.DailyCapApplied)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(o => o.WeeklyCapExceeded)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(o => o.CalculationBasis)
            .HasColumnType("text");

        // FK to the workflow engine instance — no engine exists yet (US-ADM-007), stored nullable
        // with no FK constraint. TODO(US-ADM-007): add the relationship when the engine lands.
        builder.Property(o => o.WorkflowInstanceId);

        builder.Property(o => o.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Relationships ──────────────────────────────────────────

        builder.HasOne(o => o.Employee)
            .WithMany()
            .HasForeignKey(o => o.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(o => o.AttendanceLog)
            .WithMany()
            .HasForeignKey(o => o.AttendanceLogId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes (§7) ───────────────────────────────────────────

        // Queue + report lookups: a tenant's overtime by employee/date/status.
        builder.HasIndex(o => new { o.TenantId, o.EmployeeId, o.Date })
            .HasDatabaseName("ix_overtime_record_tenant_emp_date");

        builder.HasIndex(o => new { o.TenantId, o.Status })
            .HasDatabaseName("ix_overtime_record_tenant_status");
    }
}
