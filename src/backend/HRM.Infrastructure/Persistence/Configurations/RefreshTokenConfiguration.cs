using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);

        builder.Property(rt => rt.Id)
            .HasColumnName("id");

        builder.Property(rt => rt.TokenHash)
            .HasMaxLength(500)
            .IsRequired();

        builder.HasIndex(rt => rt.TokenHash)
            .IsUnique();

        builder.Property(rt => rt.UserAgent)
            .HasMaxLength(500);

        builder.Property(rt => rt.IpAddress)
            .HasMaxLength(50);

        // Index for session queries and concurrent session counting
        builder.HasIndex(rt => new { rt.UserId, rt.TenantId, rt.RevokedAt });

        builder.HasIndex(rt => new { rt.UserId, rt.TenantId, rt.ExpiresAt });

        builder.HasOne(rt => rt.ReplacedByToken)
            .WithMany()
            .HasForeignKey(rt => rt.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
