using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the OvertimeApprovalHistory entity (US-ATT-006 FR-6/NFR-3).
/// Maps to the "overtime_approval_history" table with snake_case naming convention. Mirrors
/// <see cref="RegularizationApprovalHistoryConfiguration"/>.
/// </summary>
public sealed class OvertimeApprovalHistoryConfiguration
    : IEntityTypeConfiguration<OvertimeApprovalHistory>
{
    public void Configure(EntityTypeBuilder<OvertimeApprovalHistory> builder)
    {
        builder.ToTable("overtime_approval_history");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .HasColumnName("id");

        builder.Property(h => h.OvertimeRecordId)
            .IsRequired();

        builder.Property(h => h.ApproverEmployeeId)
            .IsRequired();

        builder.Property(h => h.ApprovalLevel)
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(h => h.Action)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(h => h.ApprovedMinutes);

        builder.Property(h => h.Comment)
            .HasColumnType("text");

        builder.Property(h => h.CalculationBasis)
            .HasColumnType("text");

        builder.Property(h => h.ActionedAt)
            .IsRequired();

        builder.Property(h => h.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Relationships ──────────────────────────────────────────

        builder.HasOne(h => h.OvertimeRecord)
            .WithMany()
            .HasForeignKey(h => h.OvertimeRecordId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.Approver)
            .WithMany()
            .HasForeignKey(h => h.ApproverEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ────────────────────────────────────────────────

        builder.HasIndex(h => new { h.TenantId, h.OvertimeRecordId })
            .HasDatabaseName("ix_overtime_approval_history_tenant_record");
    }
}
