using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="PipEvent"/> (US-PRF-008 FR-5). Maps to the "pip_event" table (singular
/// snake_case), an IMMUTABLE append-only log mirroring "review_signoff". There is deliberately NO concurrency
/// token: rows are write-once and never updated. Immutability is enforced at the service layer (no update/delete
/// path). Tenant isolation is via the global query filter in AppDbContext + TenantInterceptor.
/// </summary>
public sealed class PipEventConfiguration : IEntityTypeConfiguration<PipEvent>
{
    public void Configure(EntityTypeBuilder<PipEvent> builder)
    {
        builder.ToTable("pip_event");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id");

        builder.Property(e => e.PipId).IsRequired();

        builder.Property(e => e.EventType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(e => e.ActorUserId);
        builder.Property(e => e.ActorEmployeeId);
        builder.Property(e => e.ActorName).HasMaxLength(300).IsRequired();
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.ClientIpAddress).HasMaxLength(45);
        builder.Property(e => e.Detail).HasMaxLength(4000);

        builder.Property(e => e.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.PipId, e.OccurredAt });
    }
}
