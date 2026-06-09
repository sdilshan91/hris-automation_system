using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

public sealed class UserTenantConfiguration : IEntityTypeConfiguration<UserTenant>
{
    public void Configure(EntityTypeBuilder<UserTenant> builder)
    {
        builder.ToTable("user_tenants");

        builder.HasKey(ut => ut.Id);

        builder.Property(ut => ut.Id)
            .HasColumnName("id");

        builder.Property(ut => ut.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.HasIndex(ut => new { ut.UserId, ut.TenantId })
            .IsUnique();

        builder.HasIndex(ut => new { ut.UserId, ut.Status });

        builder.HasIndex(ut => new { ut.TenantId, ut.Status });

        builder.HasOne(ut => ut.Tenant)
            .WithMany()
            .HasForeignKey(ut => ut.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(ut => ut.UserTenantRoles)
            .WithOne(utr => utr.UserTenant)
            .HasForeignKey(utr => utr.UserTenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
