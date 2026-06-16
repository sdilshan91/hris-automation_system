using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// US-ADM-003 (FR-4): EF configuration for <see cref="ImpersonationSession"/> → table <c>impersonation_sessions</c>.
/// System-level, cross-tenant platform data: there is NO tenant global query filter (the entity is intentionally
/// not registered with one in <c>AppDbContext.OnModelCreating</c>), mirroring how the system-scoped reads work in
/// the system/admin context. The enforcement middleware loads a single session by its primary key.
/// </summary>
public sealed class ImpersonationSessionConfiguration : IEntityTypeConfiguration<ImpersonationSession>
{
    public void Configure(EntityTypeBuilder<ImpersonationSession> builder)
    {
        builder.ToTable("impersonation_sessions");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // BR-3: at most one ACTIVE session per impersonator. A partial unique index enforces it at the DB level
        // (PostgreSQL); the service also checks before insert so the failure is a clean 409 rather than an
        // unhandled constraint violation. Filtered on the Active status string written by the enum-to-string
        // conversion above.
        builder.HasIndex(s => s.ImpersonatorUserId)
            .HasFilter("status = 'Active'")
            .IsUnique();

        // Lookup by target tenant/user for the admin audit views.
        builder.HasIndex(s => new { s.TargetTenantId, s.StartedAt });
    }
}
