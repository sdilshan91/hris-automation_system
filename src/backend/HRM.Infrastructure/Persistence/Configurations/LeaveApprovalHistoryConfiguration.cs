using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the LeaveApprovalHistory entity (US-LV-005 §7, FR-5).
/// Maps to the "leave_approval_history" table with snake_case naming convention.
/// </summary>
public sealed class LeaveApprovalHistoryConfiguration : IEntityTypeConfiguration<LeaveApprovalHistory>
{
    public void Configure(EntityTypeBuilder<LeaveApprovalHistory> builder)
    {
        builder.ToTable("leave_approval_history");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .HasColumnName("id");

        builder.Property(h => h.LeaveRequestId)
            .IsRequired();

        builder.Property(h => h.ApproverEmployeeId)
            .IsRequired();

        builder.Property(h => h.ApprovalLevel)
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(h => h.Action)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(h => h.Comment)
            .HasColumnType("text");

        builder.Property(h => h.ActionedAt)
            .IsRequired();

        builder.Property(h => h.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Relationships ──────────────────────────────────────────

        builder.HasOne(h => h.LeaveRequest)
            .WithMany()
            .HasForeignKey(h => h.LeaveRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.Approver)
            .WithMany()
            .HasForeignKey(h => h.ApproverEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes (§7) ───────────────────────────────────────────

        // Primary lookup: a request's approval timeline, tenant-scoped.
        builder.HasIndex(h => new { h.TenantId, h.LeaveRequestId })
            .HasDatabaseName("ix_leave_approval_history_tenant_request");
    }
}
