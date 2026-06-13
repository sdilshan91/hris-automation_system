using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the LeaveType entity (US-LV-001).
/// Maps to the "leave_types" table with snake_case naming convention.
/// </summary>
public sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> builder)
    {
        builder.ToTable("leave_types");

        builder.HasKey(lt => lt.Id);

        builder.Property(lt => lt.Id)
            .HasColumnName("id");

        builder.Property(lt => lt.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(lt => lt.Code)
            .HasMaxLength(20);

        builder.Property(lt => lt.Color)
            .HasMaxLength(7);

        builder.Property(lt => lt.Description)
            .HasMaxLength(500);

        builder.Property(lt => lt.AnnualEntitlement)
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        builder.Property(lt => lt.AccrualFrequency)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(lt => lt.CarryForwardLimit)
            .HasColumnType("numeric(5,2)");

        builder.Property(lt => lt.CarryForwardExpiryMonths);

        builder.Property(lt => lt.ProbationEligible)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(lt => lt.DocumentsRequired)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(lt => lt.DocumentDayThreshold);

        builder.Property(lt => lt.Encashable)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(lt => lt.MaxEncashDays)
            .HasColumnType("numeric(5,2)");

        builder.Property(lt => lt.HalfDayAllowed)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(lt => lt.HourlyAllowed)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(lt => lt.Gender)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(lt => lt.MaxConsecutiveDays);

        builder.Property(lt => lt.NegativeBalanceAllowed)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(lt => lt.NegativeBalanceLimit)
            .HasColumnType("numeric(5,2)");

        builder.Property(lt => lt.DisplayOrder)
            .IsRequired();

        builder.Property(lt => lt.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(lt => lt.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // BR-1: Case-insensitive unique name per tenant.
        // Partial index excludes soft-deleted records so deactivated leave types
        // don't block reuse of the same name.
        // Note: EF Core's HasFilter generates a partial index. The LOWER() expression
        // for true case-insensitive uniqueness is enforced at the service layer because
        // EF Core doesn't support function-based indexes natively. The PostgreSQL
        // default collation (en_US.UTF-8) is typically case-sensitive, so we rely on
        // service-level case-insensitive comparison (ToLower()) as the primary guard.
        builder.HasIndex(lt => new { lt.TenantId, lt.Name })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_leave_types_tenant_id_name");

        // Performance index for list queries ordered by display_order (NFR-1).
        builder.HasIndex(lt => new { lt.TenantId, lt.IsActive, lt.DisplayOrder })
            .HasDatabaseName("ix_leave_types_tenant_id_is_active_display_order");
    }
}
