using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the Location entity (US-CHR-007).
/// Maps to the "locations" table with snake_case naming convention.
/// </summary>
public sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasColumnName("id");

        builder.Property(l => l.Name)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(l => l.AddressLine1)
            .HasMaxLength(250);

        builder.Property(l => l.AddressLine2)
            .HasMaxLength(250);

        builder.Property(l => l.City)
            .HasMaxLength(100);

        builder.Property(l => l.StateProvince)
            .HasMaxLength(100);

        builder.Property(l => l.Country)
            .HasMaxLength(100);

        builder.Property(l => l.PostalCode)
            .HasMaxLength(20);

        builder.Property(l => l.TimeZone)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(l => l.Phone)
            .HasMaxLength(20);

        builder.Property(l => l.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(l => l.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // Unique constraint: location name per tenant (FR-2, BR-1)
        // Partial index excludes soft-deleted records so deactivated locations
        // don't block reuse of the same name.
        builder.HasIndex(l => new { l.TenantId, l.Name })
            .IsUnique()
            .HasFilter("is_deleted = false");
    }
}
