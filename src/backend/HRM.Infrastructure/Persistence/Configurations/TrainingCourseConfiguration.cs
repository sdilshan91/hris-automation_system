using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="TrainingCourse"/> entity (US-TRN-001).
/// Maps to the "training_courses" table with snake_case naming convention.
/// </summary>
public sealed class TrainingCourseConfiguration : IEntityTypeConfiguration<TrainingCourse>
{
    public void Configure(EntityTypeBuilder<TrainingCourse> builder)
    {
        builder.ToTable("training_courses");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id");

        builder.Property(c => c.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.Description)
            .HasMaxLength(2000);

        builder.Property(c => c.Mode)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Capacity);

        builder.Property(c => c.Location)
            .HasMaxLength(300);

        builder.Property(c => c.Instructor)
            .HasMaxLength(200);

        builder.Property(c => c.StartDate);
        builder.Property(c => c.EndDate);

        builder.Property(c => c.DurationHours)
            .HasColumnType("numeric(7,2)");

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // NFR-1: list queries filter by (tenant, status).
        builder.HasIndex(c => new { c.TenantId, c.Status })
            .HasDatabaseName("ix_training_courses_tenant_id_status");
    }
}
