using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="Asset"/> (US-ONB-004). Maps to the "asset" table (snake_case).
/// Tenant isolation is via the global query filter in AppDbContext + TenantInterceptor. The condition and
/// status enums are stored as strings. asset_tag is unique per tenant (BR-3); serial_number uniqueness per
/// tenant (when provided) is additionally enforced in the service so the null-vs-value semantics are
/// portable across providers. assigned_employee_id is a cross-module ref (no cascade). Soft-deletable (BR-5).
/// </summary>
public sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("asset");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.AssetType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.AssetTag)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.SerialNumber).HasMaxLength(100);
        builder.Property(x => x.Brand).HasMaxLength(100);
        builder.Property(x => x.Model).HasMaxLength(100);
        builder.Property(x => x.PurchaseDate);

        builder.Property(x => x.Condition)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.AssignedEmployeeId);
        builder.Property(x => x.IssueDate);

        builder.Property(x => x.AcknowledgmentDocKey).HasMaxLength(1024);
        builder.Property(x => x.AcknowledgmentDocFileName).HasMaxLength(255);
        builder.Property(x => x.Notes).HasMaxLength(500);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // BR-3: asset_tag unique per tenant. (serial_number uniqueness enforced in the service to handle
        // the null-vs-value semantics portably across PostgreSQL + the InMemory test provider.)
        builder.HasIndex(x => new { x.TenantId, x.AssetTag }).IsUnique();

        // Common reads: available assets by type (FR-3) and an employee's assigned assets (AC-4).
        builder.HasIndex(x => new { x.TenantId, x.Status, x.AssetType });
        builder.HasIndex(x => new { x.TenantId, x.AssignedEmployeeId });

        // assigned_employee_id is a soft cross-module ref — no enforced FK / cascade.
        builder.HasOne(x => x.AssignedEmployee)
            .WithMany()
            .HasForeignKey(x => x.AssignedEmployeeId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);
    }
}
