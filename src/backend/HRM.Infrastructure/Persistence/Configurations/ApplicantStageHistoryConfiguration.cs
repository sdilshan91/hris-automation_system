using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the ApplicantStageHistory entity (US-REC-003 BR-5). Maps to the
/// "applicant_stage_history" table with snake_case naming. Tenant isolation is via the global query
/// filter in AppDbContext + TenantInterceptor (this codebase does not use Postgres RLS — same as every
/// other module). Stages are stored as strings (matching ApplicantConfiguration / the varchar status
/// pattern) for readability and forward-compat.
/// </summary>
public sealed class ApplicantStageHistoryConfiguration : IEntityTypeConfiguration<ApplicantStageHistory>
{
    public void Configure(EntityTypeBuilder<ApplicantStageHistory> builder)
    {
        builder.ToTable("applicant_stage_history");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).HasColumnName("id");

        builder.Property(h => h.ApplicantId).IsRequired();

        builder.Property(h => h.FromStage)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(h => h.ToStage)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(h => h.ChangedByUserId);

        builder.Property(h => h.Reason).HasColumnType("text");
        builder.Property(h => h.Notes).HasColumnType("text");

        builder.Property(h => h.ChangedAt).IsRequired();

        builder.Property(h => h.IsDeleted).HasDefaultValue(false).IsRequired();

        // FK to the applicant. No hard cascade across the soft-delete boundary — restrict on delete to
        // keep the audit trail intact (mirrors the codebase's audit-history patterns).
        builder.HasOne(h => h.Applicant)
            .WithMany()
            .HasForeignKey(h => h.ApplicantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Primary lookup: an applicant's stage timeline, tenant-scoped (the index the story requests).
        builder.HasIndex(h => new { h.TenantId, h.ApplicantId })
            .HasDatabaseName("ix_applicant_stage_history_tenant_applicant");
    }
}
