using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="ReviewSignoff"/> (US-PRF-006 NFR-3/BR-5/FR-7). Maps to the
/// "review_signoff" table (singular snake_case), an IMMUTABLE append-only log. Tenant isolation is via the
/// global query filter in AppDbContext + TenantInterceptor. There is deliberately NO concurrency token: rows
/// are write-once and never updated. Immutability is enforced at the service layer (no update/delete path) —
/// this config only models the columns + indexes.
/// </summary>
public sealed class ReviewSignoffConfiguration : IEntityTypeConfiguration<ReviewSignoff>
{
    public void Configure(EntityTypeBuilder<ReviewSignoff> builder)
    {
        builder.ToTable("review_signoff");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id");

        builder.Property(s => s.ManagerReviewId).IsRequired();

        builder.Property(s => s.Party)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.Action)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(s => s.SignerName).HasMaxLength(300).IsRequired();
        builder.Property(s => s.SignerUserId);
        builder.Property(s => s.SignerEmployeeId);
        builder.Property(s => s.SignedAt).IsRequired();

        // FR-7: persist the client IP captured in the controller. IPv6 fits in 45 chars.
        builder.Property(s => s.ClientIpAddress).HasMaxLength(45);

        // FR-4/FR-5/BR-4: dispute + HR-resolution comments.
        builder.Property(s => s.Comments).HasMaxLength(4000);

        builder.Property(s => s.IsDeleted).HasDefaultValue(false).IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.ManagerReviewId, s.SignedAt });
    }
}
