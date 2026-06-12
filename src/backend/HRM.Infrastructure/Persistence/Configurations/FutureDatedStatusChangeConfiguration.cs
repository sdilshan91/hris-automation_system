using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for FutureDatedStatusChange (US-CHR-009 BR-4).
/// Stores future-dated status changes that are applied by a daily Hangfire job.
/// </summary>
public sealed class FutureDatedStatusChangeConfiguration : IEntityTypeConfiguration<FutureDatedStatusChange>
{
    public void Configure(EntityTypeBuilder<FutureDatedStatusChange> builder)
    {
        builder.ToTable("future_dated_status_changes");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.FromStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.ToStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.Reason)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(e => e.EffectiveDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(e => e.RequestedBy)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(e => e.IsApplied)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.IsCancelled)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // FK to Employee
        builder.HasOne(e => e.Employee)
            .WithMany()
            .HasForeignKey(e => e.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index: find pending changes due on a given date (used by the daily Hangfire job)
        builder.HasIndex(e => new { e.TenantId, e.EffectiveDate, e.IsApplied, e.IsCancelled })
            .HasDatabaseName("ix_future_dated_status_changes_pending");

        // Index: find pending changes for a specific employee
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.IsApplied, e.IsCancelled })
            .HasDatabaseName("ix_future_dated_status_changes_employee");
    }
}
