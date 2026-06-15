using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the EmployeeSalaryComponent entity (US-PAY-002 FR-1/FR-2). Maps to the
/// "employee_salary_component" table with snake_case naming. Tenant isolation via the global query filter
/// + TenantInterceptor (Postgres RLS is the deferred US-PLT-002).
/// </summary>
public sealed class EmployeeSalaryComponentConfiguration : IEntityTypeConfiguration<EmployeeSalaryComponent>
{
    public void Configure(EntityTypeBuilder<EmployeeSalaryComponent> builder)
    {
        builder.ToTable("employee_salary_component");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.EmployeeId).IsRequired();
        builder.Property(x => x.SalaryStructureId).IsRequired();
        builder.Property(x => x.SalaryComponentId).IsRequired();

        builder.Property(x => x.AnnualAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.MonthlyAmount).HasColumnType("numeric(18,2)").IsRequired();

        builder.Property(x => x.IsOverride).HasDefaultValue(false).IsRequired();

        builder.Property(x => x.EffectiveFrom).IsRequired();
        builder.Property(x => x.EffectiveTo);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // Read path: "the employee's current/historical compensation" is queried by employee + validity.
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId });
        builder.HasIndex(x => new { x.EmployeeId, x.EffectiveTo });
        builder.HasIndex(x => x.SalaryStructureId);
    }
}
