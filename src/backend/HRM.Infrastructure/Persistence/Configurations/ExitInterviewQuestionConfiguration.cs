using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ExitInterviewQuestion"/> (US-ONB-006 FR-1). Maps to the
/// "exit_interview_question" table (snake_case). The question type is stored as a string; the multiple-choice
/// option list is a Postgres text[] column (empty for non-multiple-choice types). Tenant isolation is via the
/// global query filter in AppDbContext + TenantInterceptor.
/// </summary>
public sealed class ExitInterviewQuestionConfiguration : IEntityTypeConfiguration<ExitInterviewQuestion>
{
    public void Configure(EntityTypeBuilder<ExitInterviewQuestion> builder)
    {
        builder.ToTable("exit_interview_question");

        builder.HasKey(q => q.Id);
        builder.Property(q => q.Id).HasColumnName("id");

        builder.Property(q => q.TemplateId).IsRequired();

        builder.Property(q => q.Category)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(q => q.Text)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(q => q.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // text[] on Postgres; the InMemory test provider stores the List<string> directly.
        builder.Property(q => q.Options)
            .HasColumnType("text[]")
            .IsRequired();

        builder.Property(q => q.Required)
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(q => q.SortOrder).IsRequired();

        builder.Property(q => q.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // Ordered read of a template's questions.
        builder.HasIndex(q => new { q.TenantId, q.TemplateId, q.SortOrder });
    }
}
