using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the Employee entity (US-CHR-001, US-CHR-002).
/// Maps to the "employees" table with snake_case naming convention.
/// US-CHR-002: adds personal_email, address, xmin concurrency token, and navigations.
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

        // US-CHR-002: personal email, editable by Employee role
        builder.Property(e => e.PersonalEmail)
            .HasMaxLength(150);

        builder.Property(e => e.Phone)
            .HasMaxLength(20);

        // US-CHR-002: address, editable by Employee role
        builder.Property(e => e.Address)
            .HasMaxLength(500);

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

        builder.Property(e => e.Location)
            .HasMaxLength(200);

        builder.Property(e => e.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // US-CHR-002 FR-4: Optimistic concurrency token.
        // On PostgreSQL, this maps to the xmin system column (transaction ID of last write).
        // EF Core checks this value on UPDATE to detect concurrent modifications.
        // The property is configured as ValueGeneratedOnAddOrUpdate so the DB manages it.
        builder.Property(e => e.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

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

        // US-CHR-007: FK to Location (optional)
        builder.HasOne(e => e.LocationEntity)
            .WithMany(l => l.Employees)
            .HasForeignKey(e => e.LocationId)
            .OnDelete(DeleteBehavior.SetNull);

        // US-CHR-011: Self-referencing FK for reporting manager.
        // ON DELETE SET NULL: when a manager is deleted, direct reports' FK is nulled.
        builder.HasOne(e => e.Manager)
            .WithMany(e => e.DirectReports)
            .HasForeignKey(e => e.ReportsToEmployeeId)
            .OnDelete(DeleteBehavior.SetNull);

        // US-CHR-002: Emergency contacts collection
        builder.HasMany(e => e.EmergencyContacts)
            .WithOne(ec => ec.Employee)
            .HasForeignKey(ec => ec.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // US-CHR-002: Employment history collection
        builder.HasMany(e => e.EmploymentHistories)
            .WithOne(eh => eh.Employee)
            .HasForeignKey(eh => eh.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Indexes for common queries ──────────────────────────────

        builder.HasIndex(e => new { e.TenantId, e.DepartmentId })
            .HasDatabaseName("ix_employees_tenant_id_department_id");

        builder.HasIndex(e => new { e.TenantId, e.JobTitleId })
            .HasDatabaseName("ix_employees_tenant_id_job_title_id");

        builder.HasIndex(e => new { e.TenantId, e.Status })
            .HasDatabaseName("ix_employees_tenant_id_status");

        // US-CHR-003: indexes for directory search and filtering
        builder.HasIndex(e => new { e.TenantId, e.EmploymentType })
            .HasDatabaseName("ix_employees_tenant_id_employment_type");

        builder.HasIndex(e => new { e.TenantId, e.DateOfJoining })
            .HasDatabaseName("ix_employees_tenant_id_date_of_joining");

        builder.HasIndex(e => new { e.TenantId, e.Location })
            .HasDatabaseName("ix_employees_tenant_id_location");

        // US-CHR-011: Index for direct-reports lookups by manager
        builder.HasIndex(e => new { e.TenantId, e.ReportsToEmployeeId })
            .HasDatabaseName("ix_employees_tenant_id_reports_to_employee_id");

        // US-CHR-012 FR-11, NFR-3: GIN index on custom_fields JSONB column
        // for efficient querying of employee custom field values.
        builder.HasIndex(e => e.CustomFields)
            .HasDatabaseName("ix_employees_custom_fields_gin")
            .HasMethod("gin");
    }
}
