using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the UserInvitation entity (US-ADM-005). Maps to "user_invitations" with
/// snake_case naming. invited_role_ids → uuid[]; status stored as string. Tenant-scoped via the global
/// query filter in AppDbContext (BaseEntity-derived).
/// </summary>
public sealed class UserInvitationConfiguration : IEntityTypeConfiguration<UserInvitation>
{
    public void Configure(EntityTypeBuilder<UserInvitation> builder)
    {
        builder.ToTable("user_invitations");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasColumnName("id");

        builder.Property(i => i.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(i => i.TokenHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(i => i.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(i => i.ExpiresAt)
            .IsRequired();

        builder.Property(i => i.InvitedRoleIds)
            .HasColumnType("uuid[]")
            .IsRequired();

        builder.Property(i => i.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        // Pending-invitations tab list + per-tenant status filter.
        builder.HasIndex(i => new { i.TenantId, i.Status })
            .HasDatabaseName("ix_user_invitations_tenant_status");

        builder.HasIndex(i => new { i.TenantId, i.Email })
            .HasDatabaseName("ix_user_invitations_tenant_email");
    }
}
