using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the Interview entity (US-REC-005). Maps to the "interview" table with
/// snake_case naming. Tenant isolation is via the global query filter in AppDbContext + TenantInterceptor
/// (this codebase does not use Postgres RLS — same as every other module). Enums are stored as strings
/// (matching the codebase's varchar status/type pattern) for readability and forward-compat.
/// </summary>
public sealed class InterviewConfiguration : IEntityTypeConfiguration<Interview>
{
    public void Configure(EntityTypeBuilder<Interview> builder)
    {
        builder.ToTable("interview");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id");

        builder.Property(i => i.ApplicantId).IsRequired();
        builder.Property(i => i.VacancyId).IsRequired();

        builder.Property(i => i.RoundNumber).IsRequired();

        builder.Property(i => i.InterviewType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.ScheduledDate).IsRequired();
        builder.Property(i => i.StartTime).IsRequired();
        builder.Property(i => i.DurationMinutes).IsRequired();

        builder.Property(i => i.Location).HasMaxLength(300);
        builder.Property(i => i.VideoLink).HasMaxLength(500);
        builder.Property(i => i.Notes).HasColumnType("text");

        builder.Property(i => i.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.ReminderJobId).HasMaxLength(64);

        builder.Property(i => i.IsDeleted).HasDefaultValue(false).IsRequired();

        // Ignore computed-only helpers (not persisted).
        builder.Ignore(i => i.StartsAtUtc);
        builder.Ignore(i => i.EndsAtUtc);

        builder.HasMany(i => i.Interviewers)
            .WithOne(ii => ii.Interview)
            .HasForeignKey(ii => ii.InterviewId)
            .OnDelete(DeleteBehavior.Cascade);

        // Story-requested indexes: applicant timeline + calendar (date-range) query.
        builder.HasIndex(i => new { i.TenantId, i.ApplicantId })
            .HasDatabaseName("ix_interview_tenant_applicant");

        builder.HasIndex(i => new { i.TenantId, i.ScheduledDate })
            .HasDatabaseName("ix_interview_tenant_scheduled_date");
    }
}
