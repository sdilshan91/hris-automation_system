using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnName("id");

        builder.Property(u => u.Email)
            .HasMaxLength(150)
            .IsRequired();

        builder.HasIndex(u => u.Email)
            .IsUnique();

        builder.Property(u => u.DisplayName)
            .HasMaxLength(200);

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(500);

        // BUG-040: SHA-256 hex of the active password-reset token (64 chars).
        builder.Property(u => u.PasswordResetTokenHash)
            .HasMaxLength(64);

        // CR-AUTH-001: Entra SSO identity link. EntraObjectId is unique when present (filtered unique
        // index — many local users have null and must not collide).
        builder.Property(u => u.EntraObjectId)
            .HasMaxLength(64);

        builder.HasIndex(u => u.EntraObjectId)
            .IsUnique()
            .HasFilter("entra_object_id IS NOT NULL");

        builder.Property(u => u.IdentityProvider)
            .HasMaxLength(20);

        builder.Property(u => u.MfaSecret)
            .HasMaxLength(200);

        builder.Property(u => u.MfaEnabled)
            .HasDefaultValue(false);

        builder.Property(u => u.MfaFailedAttemptCount)
            .HasDefaultValue(0);

        builder.Property(u => u.FailedLoginCount)
            .HasDefaultValue(0);

        builder.Property(u => u.LockoutCount)
            .HasDefaultValue(0);

        builder.Property(u => u.LastLockoutAt);

        builder.HasMany(u => u.UserTenants)
            .WithOne(ut => ut.User)
            .HasForeignKey(ut => ut.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.RefreshTokens)
            .WithOne(rt => rt.User)
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
