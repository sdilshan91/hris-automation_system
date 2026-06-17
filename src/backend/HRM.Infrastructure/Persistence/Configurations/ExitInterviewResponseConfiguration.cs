using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ExitInterviewResponse"/> (US-ONB-006 §7). Maps to the
/// "exit_interview_response" table (snake_case). The category snapshot supports tenant-scoped analytics
/// grouping (FR-4); free_text is PII (FR-5/NFR-6). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor.
/// </summary>
public sealed class ExitInterviewResponseConfiguration : IEntityTypeConfiguration<ExitInterviewResponse>
{
    public void Configure(EntityTypeBuilder<ExitInterviewResponse> builder)
    {
        builder.ToTable("exit_interview_response");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ExitInterviewId).IsRequired();
        builder.Property(x => x.QuestionId).IsRequired();

        builder.Property(x => x.Category)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Rating);
        builder.Property(x => x.SelectedOption).HasMaxLength(200);
        builder.Property(x => x.FreeText).HasMaxLength(2000);

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // Analytics aggregation read (FR-4): per-tenant, by interview.
        builder.HasIndex(x => new { x.TenantId, x.ExitInterviewId });
    }
}
