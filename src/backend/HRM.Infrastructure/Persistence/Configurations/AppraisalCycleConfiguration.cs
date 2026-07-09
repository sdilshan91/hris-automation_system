using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="AppraisalCycle"/>. Originally a minimal entity (US-PRF-001..003),
/// fleshed out by US-PRF-004 into full cycle management (type, overall window, full status enum, feature
/// toggles, cancellation reason). The legacy window columns are retained (kept in sync with phases).
/// Maps to the "appraisal_cycle" table with snake_case naming. Tenant isolation is via the global query
/// filter in AppDbContext + TenantInterceptor (no Postgres RLS — same as every other module).
/// </summary>
public sealed class AppraisalCycleConfiguration : IEntityTypeConfiguration<AppraisalCycle>
{
    public void Configure(EntityTypeBuilder<AppraisalCycle> builder)
    {
        builder.ToTable("appraisal_cycle");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");

        builder.Property(c => c.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(Domain.Enums.CycleType.Annual)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // US-PRF-004: overall cycle window. Defaults to epoch for existing rows via the migration default;
        // new cycles set them explicitly.
        builder.Property(c => c.StartDate).IsRequired();
        builder.Property(c => c.EndDate).IsRequired();

        // Legacy phase windows (US-PRF-001/002/003) — kept in sync with the cycle_phase rows on save.
        builder.Property(c => c.GoalSettingStart).IsRequired();
        builder.Property(c => c.GoalSettingEnd).IsRequired();
        builder.Property(c => c.SelfAssessmentStart).IsRequired();
        builder.Property(c => c.SelfAssessmentEnd).IsRequired();
        builder.Property(c => c.ManagerReviewStart).IsRequired();
        builder.Property(c => c.ManagerReviewEnd).IsRequired();

        builder.Property(c => c.RatingScaleMax).HasDefaultValue(5).IsRequired();
        builder.Property(c => c.SelfWeightPercent).HasDefaultValue(30).IsRequired();

        // US-PRF-006: sign-off config (BR-3 auto-close window + tenant meeting-notes template, §10).
        builder.Property(c => c.SignoffAutoCloseDays).HasDefaultValue(7).IsRequired();
        builder.Property(c => c.MeetingNotesTemplate);

        // US-PRF-004 feature toggles (FR-6).
        builder.Property(c => c.Is360Enabled).HasDefaultValue(false).IsRequired();
        builder.Property(c => c.IsCalibrationEnabled).HasDefaultValue(false).IsRequired();
        builder.Property(c => c.IsAnonymousFeedback).HasDefaultValue(false).IsRequired();

        // US-PRF-005: 360 composite-score per-category weights (FR-6) + min-peer threshold (BR-4/FR-3).
        builder.Property(c => c.ThreeSixtySelfWeightPercent).HasDefaultValue(10).IsRequired();
        builder.Property(c => c.ThreeSixtyManagerWeightPercent).HasDefaultValue(40).IsRequired();
        builder.Property(c => c.ThreeSixtyPeerWeightPercent).HasDefaultValue(30).IsRequired();
        builder.Property(c => c.ThreeSixtyReportWeightPercent).HasDefaultValue(20).IsRequired();
        builder.Property(c => c.Min360PeerReviewers).HasDefaultValue(2).IsRequired();

        builder.Property(c => c.ParticipantScope)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(Domain.Enums.ParticipantScopeType.AllEmployees)
            .IsRequired();

        // BUG-260: the selected department ids for a Departments-scoped cycle, persisted so the edit form can
        // prefill the selection. EF Core 10 maps List<Guid> as a primitive collection; force a jsonb column
        // (snake_case "scope_department_ids") rather than a Postgres uuid[].
        builder.Property(c => c.ScopeDepartmentIds)
            .HasColumnType("jsonb");

        builder.Property(c => c.CancellationReason).HasMaxLength(1000);

        builder.Property(c => c.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasMany(c => c.Phases)
            .WithOne(p => p.Cycle!)
            .HasForeignKey(p => p.CycleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Participants)
            .WithOne(p => p.Cycle!)
            .HasForeignKey(p => p.CycleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.TenantId, c.Status });
        builder.HasIndex(c => new { c.TenantId, c.Type, c.Status });
    }
}
