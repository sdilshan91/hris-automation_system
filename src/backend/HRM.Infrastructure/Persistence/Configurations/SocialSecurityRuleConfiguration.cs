using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="SocialSecurityRule"/> entity (US-PAY-006). Maps to the
/// "social_security_rule" table with snake_case naming. <c>applicable_component_ids</c> is a Postgres
/// uuid[] column (Npgsql maps a <c>List&lt;Guid&gt;</c> natively). Tenant isolation is via the global query
/// filter + TenantInterceptor.
/// </summary>
public sealed class SocialSecurityRuleConfiguration : IEntityTypeConfiguration<SocialSecurityRule>
{
    public void Configure(EntityTypeBuilder<SocialSecurityRule> builder)
    {
        builder.ToTable("social_security_rule");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.StatutoryRuleId).IsRequired();

        // Rates — numeric(5,2); ceiling — numeric(18,2) (§7/§10).
        builder.Property(s => s.EmployeeRate).HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(s => s.EmployerRate).HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(s => s.WageCeilingAnnual).HasColumnType("numeric(18,2)");

        builder.Property(s => s.ApplicableOn)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Postgres uuid[] (Npgsql provider). Nullable for non-Custom bases. The InMemory provider used by
        // integration tests tolerates the CLR List<Guid> directly.
        builder.Property(s => s.ApplicableComponentIds)
            .HasColumnType("uuid[]");

        builder.Property(s => s.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.StatutoryRuleId }).IsUnique();
    }
}
