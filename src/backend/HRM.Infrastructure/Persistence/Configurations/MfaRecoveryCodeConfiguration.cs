using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

public sealed class MfaRecoveryCodeConfiguration : IEntityTypeConfiguration<MfaRecoveryCode>
{
    public void Configure(EntityTypeBuilder<MfaRecoveryCode> builder)
    {
        builder.ToTable("mfa_recovery_codes");

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id");

        builder.Property(m => m.UserId)
            .IsRequired();

        builder.Property(m => m.CodeHash)
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(m => m.UserId);

        // Composite index for fast unused-code lookup
        builder.HasIndex(m => new { m.UserId, m.UsedAt });

        builder.HasOne(m => m.User)
            .WithMany(u => u.MfaRecoveryCodes)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
