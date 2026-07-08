using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// US-AUTH-004 FR-5 (ISSUE-053): password-history table. Not tenant-scoped (mirrors the global User).
/// </summary>
public sealed class PasswordHistoryConfiguration : IEntityTypeConfiguration<PasswordHistory>
{
    public void Configure(EntityTypeBuilder<PasswordHistory> builder)
    {
        builder.ToTable("password_history");

        builder.HasKey(ph => ph.Id);

        builder.Property(ph => ph.Id)
            .HasColumnName("id");

        builder.Property(ph => ph.UserId)
            .IsRequired();

        builder.Property(ph => ph.PasswordHash)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(ph => ph.CreatedAt)
            .IsRequired();

        // Fast newest-first lookup + prune-by-age for a given user.
        builder.HasIndex(ph => new { ph.UserId, ph.CreatedAt });

        builder.HasOne(ph => ph.User)
            .WithMany()
            .HasForeignKey(ph => ph.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
