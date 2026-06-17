using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ExitInterview"/> (US-ONB-006). Maps to the "exit_interview" table
/// (snake_case). The interview mode is stored as a string; offboarding_instance_id / template_id are
/// cross-ref ids. Tenant isolation is via the global query filter in AppDbContext + TenantInterceptor
/// (FR-6/NFR-2/AC-5). BR-1 (one active interview per offboarding) is enforced in the service + a partial
/// unique index over non-superseded rows.
/// </summary>
public sealed class ExitInterviewConfiguration : IEntityTypeConfiguration<ExitInterview>
{
    public void Configure(EntityTypeBuilder<ExitInterview> builder)
    {
        builder.ToTable("exit_interview");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.OffboardingInstanceId).IsRequired();
        builder.Property(x => x.TemplateId).IsRequired();

        builder.Property(x => x.InterviewMode)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.ConductedByUserId);
        builder.Property(x => x.InterviewDate).IsRequired();
        builder.Property(x => x.OverallExperienceRating);
        builder.Property(x => x.WouldRecommendEmployer);
        builder.Property(x => x.AdditionalComments).HasMaxLength(5000);

        builder.Property(x => x.Version).HasDefaultValue(1).IsRequired();
        builder.Property(x => x.IsSuperseded).HasDefaultValue(false).IsRequired();
        builder.Property(x => x.SupersedesId);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // BR-1: at most one ACTIVE (non-superseded, non-deleted) exit interview per offboarding instance.
        builder.HasIndex(x => new { x.TenantId, x.OffboardingInstanceId })
            .IsUnique()
            .HasFilter("is_superseded = false AND is_deleted = false");

        builder.HasMany(x => x.Responses)
            .WithOne(r => r.ExitInterview!)
            .HasForeignKey(r => r.ExitInterviewId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
