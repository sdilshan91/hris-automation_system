using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="PayrollAdjustment"/> entity (US-PAY-007). Maps to the
/// "payroll_adjustment" table with snake_case naming. Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor (Postgres RLS is the deferred US-PLT-002).
/// </summary>
public sealed class PayrollAdjustmentConfiguration : IEntityTypeConfiguration<PayrollAdjustment>
{
    public void Configure(EntityTypeBuilder<PayrollAdjustment> builder)
    {
        builder.ToTable("payroll_adjustment");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");

        builder.Property(a => a.EmployeeId).IsRequired();

        // Enums stored as strings (matches the codebase varchar pattern + the global JsonStringEnumConverter).
        builder.Property(a => a.AdjustmentType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Money — numeric(18,2) per the data spec.
        builder.Property(a => a.Amount).HasColumnType("numeric(18,2)").IsRequired();

        builder.Property(a => a.Description).IsRequired();

        builder.Property(a => a.ApplicablePayMonth).IsRequired();
        builder.Property(a => a.ApplicablePayYear).IsRequired();

        builder.Property(a => a.IsTaxable).HasDefaultValue(false).IsRequired();
        builder.Property(a => a.IsRecurring).HasDefaultValue(false).IsRequired();

        builder.Property(a => a.RecurrenceEndMonth);
        builder.Property(a => a.RecurrenceEndYear);

        builder.Property(a => a.AppliedInPayrollRunId);
        builder.Property(a => a.ReferencePayrollSlipId);
        builder.Property(a => a.RecurringSeriesId);

        builder.Property(a => a.SupportingDocumentPath).HasMaxLength(500);

        builder.Property(a => a.IsDeleted).HasDefaultValue(false).IsRequired();

        // The run engine fetches Pending adjustments for a (tenant, period) — the hot path (FR-3).
        builder.HasIndex(a => new { a.TenantId, a.ApplicablePayYear, a.ApplicablePayMonth, a.Status });
        // List/filter by employee (§8 filters).
        builder.HasIndex(a => new { a.TenantId, a.EmployeeId });
        // Cancel-a-recurring-series lookups (BR-6).
        builder.HasIndex(a => new { a.TenantId, a.RecurringSeriesId });
    }
}
