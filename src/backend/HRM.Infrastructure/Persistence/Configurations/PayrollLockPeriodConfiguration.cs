using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the PayrollLockPeriod placeholder entity (US-ATT-003 AC-5/FR-7/BR-6).
/// Maps to the "payroll_lock_period" table. The Payroll module will own this concept later
/// (TODO US-PAY); for now it is the minimal seam the regularization lock check consults.
/// </summary>
public sealed class PayrollLockPeriodConfiguration : IEntityTypeConfiguration<PayrollLockPeriod>
{
    public void Configure(EntityTypeBuilder<PayrollLockPeriod> builder)
    {
        builder.ToTable("payroll_lock_period");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.Property(p => p.StartDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.EndDate)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.StartDate, p.EndDate })
            .HasDatabaseName("ix_payroll_lock_period_tenant_range");
    }
}
