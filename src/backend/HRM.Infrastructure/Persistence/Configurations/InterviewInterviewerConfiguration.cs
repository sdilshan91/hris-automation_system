using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the InterviewInterviewer junction entity (US-REC-005 FR-1). Maps to the
/// "interview_interviewer" table with snake_case naming. Tenant isolation is via the global query filter
/// in AppDbContext + TenantInterceptor (no Postgres RLS — same as every other module).
/// </summary>
public sealed class InterviewInterviewerConfiguration : IEntityTypeConfiguration<InterviewInterviewer>
{
    public void Configure(EntityTypeBuilder<InterviewInterviewer> builder)
    {
        builder.ToTable("interview_interviewer");

        builder.HasKey(ii => ii.Id);
        builder.Property(ii => ii.Id).HasColumnName("id");

        builder.Property(ii => ii.InterviewId).IsRequired();
        builder.Property(ii => ii.EmployeeId).IsRequired();

        builder.Property(ii => ii.IsDeleted).HasDefaultValue(false).IsRequired();

        // An interviewer appears at most once per interview (no duplicate links). Partial — exclude
        // soft-deleted so a removed-then-re-added interviewer is allowed.
        builder.HasIndex(ii => new { ii.TenantId, ii.InterviewId, ii.EmployeeId })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ux_interview_interviewer_tenant_interview_employee");

        // Conflict detection (FR-7) queries by interviewer across interviews.
        builder.HasIndex(ii => new { ii.TenantId, ii.EmployeeId })
            .HasDatabaseName("ix_interview_interviewer_tenant_employee");
    }
}
