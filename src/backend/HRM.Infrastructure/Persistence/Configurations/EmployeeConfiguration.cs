using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence.Converters;
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

        // US-CHR-013: full-time equivalent. numeric(3,2) holds (0, 1.00] at 2 dp. The DB default of 1.00
        // backfills every existing row, so leave proration (x 1.0) is unchanged.
        builder.Property(e => e.Fte)
            .HasColumnType("numeric(3,2)")
            .HasDefaultValue(1.00m)
            .IsRequired();

        // US-CHR-013 / US-ATT-011 AC-5: work arrangement, stored as its integer value (OnSite = 0) so the
        // DB default backfills existing rows to today's fully-enforced geo-fence behaviour.
        builder.Property(e => e.WorkArrangement)
            .HasDefaultValue(WorkArrangement.OnSite)
            .IsRequired();

        builder.Property(e => e.ProfilePhotoUrl)
            .HasMaxLength(500);

        builder.Property(e => e.CustomFields)
            .HasColumnType("jsonb");

        builder.Property(e => e.Location)
            .HasMaxLength(200);

        // US-RPT-003 AC-4 / FR-6: bank-detail fields for the Bank Advice report (nullable until captured).
        builder.Property(e => e.BankName)
            .HasMaxLength(150);

        builder.Property(e => e.BankBranchCode)
            .HasMaxLength(50);

        builder.Property(e => e.BankAccountNumber)
            .HasMaxLength(50);

        // ISSUE-293: national identity number. Plain (non-encrypted-build) config only declares the property;
        // the AES-at-rest converter + `text` column type are applied by ApplyEncryption (invoked from
        // AppDbContext.OnModelCreating with the injected IFieldEncryptor). HasMaxLength is intentionally NOT set:
        // the ciphertext is longer than 50 chars and ApplyEncryption maps the column to unbounded `text`.
        builder.Property(e => e.NationalId);

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

    /// <summary>
    /// ISSUE-293: applies the field-at-rest encryption value converter to <see cref="Employee.NationalId"/>
    /// (PII, mirrors the Pip/Recommendation pattern). Invoked from <c>AppDbContext.OnModelCreating</c> with the
    /// context's injected <see cref="IFieldEncryptor"/> (this config is parameterless so the assembly-scan is
    /// unaffected). The column is <c>text</c> — the AES-GCM ciphertext is longer than the 50-char plaintext, so
    /// no length cap is applied. ⚠ The converter MUST be wired here + invoked from AppDbContext: a converter in
    /// the parameterless Configure alone would silently store PLAINTEXT.
    /// </summary>
    public static void ApplyEncryption(EntityTypeBuilder<Employee> builder, IFieldEncryptor encryptor)
    {
        builder.Property(e => e.NationalId)
            .HasConversion(EncryptedFieldConverters.NullableString(encryptor))
            .HasColumnType("text");
    }
}
