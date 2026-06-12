using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the Employee entity (US-CHR-001).
/// Maps to the "employees" table with snake_case naming convention.
/// </summary>
public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.EmployeeNo)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.FirstName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.LastName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Email)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(e => e.Phone)
            .HasMaxLength(20);

        builder.Property(e => e.DateOfBirth)
            .HasColumnType("date");

        builder.Property(e => e.Gender)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(e => e.DateOfJoining)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(e => e.EmploymentType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(EmployeeStatus.Active)
            .IsRequired();

        builder.Property(e => e.ProfilePhotoUrl)
            .HasMaxLength(500);

        builder.Property(e => e.CustomFields)
            .HasColumnType("jsonb");

        builder.Property(e => e.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // ── Unique constraints ──────────────────────────────────────

        // FR-2 / BR-1: employee_no unique per tenant
        builder.HasIndex(e => new { e.TenantId, e.EmployeeNo })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // FR-3 / BR-2: email unique per tenant
        builder.HasIndex(e => new { e.TenantId, e.Email })
            .IsUnique()
            .HasFilter("is_deleted = false");

        // ── Foreign keys ────────────────────────────────────────────

        // FK to Department (required)
        builder.HasOne(e => e.Department)
            .WithMany(d => d.Employees)
            .HasForeignKey(e => e.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK to JobTitle (required)
        builder.HasOne(e => e.JobTitle)
            .WithMany()
            .HasForeignKey(e => e.JobTitleId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK to User (optional, for portal access)
        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Indexes for common queries ──────────────────────────────

        builder.HasIndex(e => new { e.TenantId, e.DepartmentId })
            .HasDatabaseName("ix_employees_tenant_id_department_id");

        builder.HasIndex(e => new { e.TenantId, e.JobTitleId })
            .HasDatabaseName("ix_employees_tenant_id_job_title_id");

        builder.HasIndex(e => new { e.TenantId, e.Status })
            .HasDatabaseName("ix_employees_tenant_id_status");
    }
}
