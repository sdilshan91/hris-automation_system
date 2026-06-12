using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for EmergencyContact (US-CHR-002).
/// </summary>
public sealed class EmergencyContactConfiguration : IEntityTypeConfiguration<EmergencyContact>
{
    public void Configure(EntityTypeBuilder<EmergencyContact> builder)
    {
        builder.ToTable("emergency_contacts");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.ContactName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Relationship)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Phone)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.AlternatePhone)
            .HasMaxLength(20);

        builder.Property(e => e.Email)
            .HasMaxLength(150);

        builder.Property(e => e.IsPrimary)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // Index for efficient queries by employee
        builder.HasIndex(e => new { e.TenantId, e.EmployeeId })
            .HasDatabaseName("ix_emergency_contacts_tenant_employee");
    }
}
