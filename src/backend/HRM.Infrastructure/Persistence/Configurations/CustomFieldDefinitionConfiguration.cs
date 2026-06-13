using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the CustomFieldDefinition entity (US-CHR-012).
/// Maps to the "custom_field_definitions" table with snake_case naming convention.
/// </summary>
public sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.ToTable("custom_field_definitions");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id");

        builder.Property(c => c.EntityType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(c => c.FieldName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.FieldKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.FieldType)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(c => c.IsRequired)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(c => c.Options)
            .HasColumnType("jsonb");

        builder.Property(c => c.DisplayOrder)
            .IsRequired();

        builder.Property(c => c.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(c => c.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // BR-1: field_name unique per tenant + entity_type (excluding soft-deleted)
        builder.HasIndex(c => new { c.TenantId, c.EntityType, c.FieldName })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_custom_field_definitions_tenant_entity_name");

        // field_key unique per tenant + entity_type (excluding soft-deleted)
        builder.HasIndex(c => new { c.TenantId, c.EntityType, c.FieldKey })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_custom_field_definitions_tenant_entity_key");
    }
}
