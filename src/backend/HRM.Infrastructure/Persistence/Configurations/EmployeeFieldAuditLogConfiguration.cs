using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for EmployeeFieldAuditLog (US-CHR-002 FR-5).
/// Field-level change audit with JSONB before/after snapshots.
/// </summary>
public sealed class EmployeeFieldAuditLogConfiguration : IEntityTypeConfiguration<EmployeeFieldAuditLog>
{
    public void Configure(EntityTypeBuilder<EmployeeFieldAuditLog> builder)
    {
        builder.ToTable("employee_field_audit_logs");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.Section)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.BeforeSnapshot)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.AfterSnapshot)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.ChangedBy)
            .HasMaxLength(150)
            .IsRequired();

        // Indexes for efficient audit queries
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId, e.CreatedAt })
            .HasDatabaseName("ix_employee_field_audit_logs_tenant_employee_created");

        builder.HasIndex(e => new { e.TenantId, e.CreatedAt })
            .HasDatabaseName("ix_employee_field_audit_logs_tenant_created");
    }
}
