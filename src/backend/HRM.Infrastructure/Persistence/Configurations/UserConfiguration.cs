using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>
    /// Storage width of <c>users.mfa_secret</c>. Exposed as a constant because the US-PLT-005 legacy back-fill
    /// (<c>DbInitializer.BackfillLegacyMfaSecretsAsync</c>) must reject an over-long protected payload BEFORE
    /// writing it — that back-fill runs at application startup, so an unguarded overflow would surface as
    /// <c>value too long for type character varying(512)</c> during boot and take the whole service down.
    /// Keep this and the <see cref="Configure"/> mapping in lock-step; they must never drift.
    /// </summary>
    public const int MfaSecretMaxLength = 512;

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

        // US-AUTH-005 NFR-2: stores the Data-Protection-encrypted TOTP secret (a ~32-byte secret protects to
        // several hundred chars). Widened from 200 to 512 to hold the protected payload (legacy plaintext fits too).
        builder.Property(u => u.MfaSecret)
            .HasMaxLength(MfaSecretMaxLength);

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
