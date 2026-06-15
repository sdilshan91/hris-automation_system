using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the PayrollSlipDetail entity (US-PAY-003 FR-5f). Maps to the
/// "payroll_slip_detail" table with snake_case naming. Tenant isolation via the global query filter +
/// TenantInterceptor. Component name/type are denormalized for historical stability.
/// </summary>
public sealed class PayrollSlipDetailConfiguration : IEntityTypeConfiguration<PayrollSlipDetail>
{
    public void Configure(EntityTypeBuilder<PayrollSlipDetail> builder)
    {
        builder.ToTable("payroll_slip_detail");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.PayrollSlipId).IsRequired();
        builder.Property(x => x.SalaryComponentId).IsRequired();

        builder.Property(x => x.ComponentName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ComponentType).HasMaxLength(20).IsRequired();

        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.CalculationBasis).HasColumnType("text");

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.PayrollSlipId });
    }
}
