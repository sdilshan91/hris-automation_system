using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the RegularizationApprovalHistory entity (US-ATT-004 FR-6/NFR-4).
/// Maps to the "attendance_regularization_history" table with snake_case naming convention. Mirrors
/// <see cref="LeaveApprovalHistoryConfiguration"/>.
/// </summary>
public sealed class RegularizationApprovalHistoryConfiguration
    : IEntityTypeConfiguration<RegularizationApprovalHistory>
{
    public void Configure(EntityTypeBuilder<RegularizationApprovalHistory> builder)
    {
        builder.ToTable("attendance_regularization_history");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .HasColumnName("id");

        builder.Property(h => h.RegularizationId)
            .IsRequired();

        builder.Property(h => h.ApproverEmployeeId)
            .IsRequired();

        builder.Property(h => h.ApprovalLevel)
            .HasDefaultValue(1)
            .IsRequired();

        builder.Property(h => h.Action)
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

        builder.HasOne(h => h.Regularization)
            .WithMany()
            .HasForeignKey(h => h.RegularizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(h => h.Approver)
            .WithMany()
            .HasForeignKey(h => h.ApproverEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes (§7) ───────────────────────────────────────────

        // Primary lookup: a regularization's decision timeline, tenant-scoped.
        builder.HasIndex(h => new { h.TenantId, h.RegularizationId })
            .HasDatabaseName("ix_attendance_regularization_history_tenant_reg");
    }
}
