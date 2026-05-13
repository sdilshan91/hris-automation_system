using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

public sealed class UserTenantRoleConfiguration : IEntityTypeConfiguration<UserTenantRole>
{
    public void Configure(EntityTypeBuilder<UserTenantRole> builder)
    {
        builder.ToTable("user_tenant_roles");

        builder.HasKey(utr => new { utr.UserTenantId, utr.RoleId });

        builder.HasOne(utr => utr.Role)
            .WithMany(r => r.UserTenantRoles)
            .HasForeignKey(utr => utr.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(utr => utr.AssignedBy)
            .HasMaxLength(200);
    }
}
