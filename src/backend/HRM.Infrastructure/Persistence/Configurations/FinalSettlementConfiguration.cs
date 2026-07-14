using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="FinalSettlement"/> + <see cref="FinalSettlementLine"/> (ISSUE-294
/// Phase 1). Maps to "final_settlement" / "final_settlement_line" with snake_case naming. The UNIQUE index on
/// offboarding_instance_id is the idempotency guarantee (a retried offboarding-complete cannot double-create).
/// Tenant isolation is via the AppDbContext global query filter + TenantInterceptor + the dormant Postgres RLS
/// policies shipped in the migration.
/// </summary>
public sealed class FinalSettlementConfiguration : IEntityTypeConfiguration<FinalSettlement>
{
    public void Configure(EntityTypeBuilder<FinalSettlement> builder)
    {
        builder.ToTable("final_settlement");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.EmployeeId).IsRequired();
        builder.Property(s => s.OffboardingInstanceId).IsRequired();
        builder.Property(s => s.LastWorkingDay).IsRequired();
        builder.Property(s => s.CountryCode).HasMaxLength(5);
        builder.Property(s => s.FiscalYear).HasMaxLength(10).IsRequired();

        builder.Property(s => s.ProRatedGross).HasColumnType("numeric(18,2)");
        builder.Property(s => s.StatutoryTotal).HasColumnType("numeric(18,2)");
        builder.Property(s => s.LeaveEncashmentTotal).HasColumnType("numeric(18,2)");
        builder.Property(s => s.NetPayable).HasColumnType("numeric(18,2)");

        builder.Property(s => s.PolicyEffectiveFrom).IsRequired();
        builder.Property(s => s.FinalPeriodOwnedBySettlement).HasDefaultValue(false).IsRequired();
        builder.Property(s => s.StatutorySkipped).HasDefaultValue(false).IsRequired();
        builder.Property(s => s.Notes);
        builder.Property(s => s.ComputedAtUtc).IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.IsDeleted).HasDefaultValue(false).IsRequired();

        // Idempotency: at most ONE settlement per offboarding instance. Filtered on is_deleted = false so a
        // soft-deleted settlement does not permanently block re-creating one for the same instance.
        builder.HasIndex(s => s.OffboardingInstanceId)
            .IsUnique()
            .HasFilter("is_deleted = false");

        // The run double-pay guard scans settlements by LWD within a period.
        builder.HasIndex(s => new { s.TenantId, s.LastWorkingDay });

        builder.HasMany(s => s.Lines)
            .WithOne(l => l.Settlement!)
            .HasForeignKey(l => l.FinalSettlementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>EF Core configuration for <see cref="FinalSettlementLine"/> (ISSUE-294 Phase 1).</summary>
public sealed class FinalSettlementLineConfiguration : IEntityTypeConfiguration<FinalSettlementLine>
{
    public void Configure(EntityTypeBuilder<FinalSettlementLine> builder)
    {
        builder.ToTable("final_settlement_line");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id");

        builder.Property(l => l.FinalSettlementId).IsRequired();
        builder.Property(l => l.Label).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Amount).HasColumnType("numeric(18,2)");

        builder.Property(l => l.Type)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(l => l.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(l => l.FinalSettlementId);
    }
}
