using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the <see cref="CourseEnrollment"/> entity (US-TRN-001).
/// Maps to the "course_enrollments" table with snake_case naming convention.
/// </summary>
public sealed class CourseEnrollmentConfiguration : IEntityTypeConfiguration<CourseEnrollment>
{
    public void Configure(EntityTypeBuilder<CourseEnrollment> builder)
    {
        builder.ToTable("course_enrollments");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id");

        builder.Property(e => e.CourseId)
            .IsRequired();

        builder.Property(e => e.EmployeeId)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.EnrolledAt)
            .IsRequired();

        builder.Property(e => e.EnrolledBy)
            .IsRequired();

        builder.Property(e => e.WaitlistPosition);
        builder.Property(e => e.CancelledAt);
        builder.Property(e => e.CompletedAt);

        builder.Property(e => e.CertificateReference)
            .HasMaxLength(500);

        builder.Property(e => e.Score)
            .HasColumnType("numeric(6,2)");

        builder.Property(e => e.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // FK → training_courses. No cascade (courses are soft-deleted; enrollments retained for history).
        builder.HasOne<TrainingCourse>()
            .WithMany()
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        // BR-2 (AC-6): one ACTIVE (Enrolled/Waitlisted) enrollment per (course, employee). Partial unique index
        // excludes soft-deleted and terminal (Cancelled/Completed/NoShow) rows so a re-enroll after cancel/complete
        // is allowed. Duplicate active enrollment → the service returns 409 before hitting this DB guard.
        builder.HasIndex(e => new { e.TenantId, e.CourseId, e.EmployeeId })
            .IsUnique()
            .HasFilter("is_deleted = false AND status IN ('Enrolled','Waitlisted')")
            .HasDatabaseName("ix_course_enrollments_tenant_course_employee_active");

        // Waitlist ordering: fetch the FIFO head per course/status quickly (BR-3).
        builder.HasIndex(e => new { e.TenantId, e.CourseId, e.Status, e.WaitlistPosition })
            .HasDatabaseName("ix_course_enrollments_tenant_course_status_position");
    }
}
