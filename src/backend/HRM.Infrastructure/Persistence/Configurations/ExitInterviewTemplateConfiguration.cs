using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ExitInterviewTemplate"/> (US-ONB-006 FR-1/BR-6). Maps to the
/// "exit_interview_template" table (snake_case). Tenant isolation is via the global query filter in
/// AppDbContext + TenantInterceptor (no Postgres RLS — same as every other module).
/// </summary>
public sealed class ExitInterviewTemplateConfiguration : IEntityTypeConfiguration<ExitInterviewTemplate>
{
    public void Configure(EntityTypeBuilder<ExitInterviewTemplate> builder)
    {
        builder.ToTable("exit_interview_template");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.TemplateName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(t => t.Description)
            .HasMaxLength(2000);

        builder.Property(t => t.IsActive)
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(t => t.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // Tenant-scoped lookup of the active template for new interviews (FR-1/AC-1).
        builder.HasIndex(t => new { t.TenantId, t.IsActive });

        builder.HasMany(t => t.Questions)
            .WithOne(q => q.Template!)
            .HasForeignKey(q => q.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
