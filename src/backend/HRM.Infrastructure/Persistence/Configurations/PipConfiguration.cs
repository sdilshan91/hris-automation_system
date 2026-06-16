using HRM.Domain.Performance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="Pip"/> (US-PRF-008). Maps to the "pip" table (singular snake_case).
/// Tenant isolation is via the global query filter in AppDbContext + TenantInterceptor (no Postgres RLS — same
/// as every other module). BR-2 (one active PIP per employee) is enforced by a tenant-scoped PARTIAL unique
/// index over the non-terminal statuses plus a defensive service check. Optimistic concurrency rides the Npgsql
/// xmin row-version convention.
/// </summary>
public sealed class PipConfiguration : IEntityTypeConfiguration<Pip>
{
    public void Configure(EntityTypeBuilder<Pip> builder)
    {
        builder.ToTable("pip");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id");

        builder.Property(p => p.EmployeeId).IsRequired();
        builder.Property(p => p.ManagerEmployeeId);
        builder.Property(p => p.MentorEmployeeId);
        builder.Property(p => p.OriginManagerReviewId);

        // SENSITIVE (NFR-4 encryption seam — stored plain text today; see Pip xmldoc).
        builder.Property(p => p.Reason).HasMaxLength(4000).IsRequired();

        builder.Property(p => p.StartDate).IsRequired();
        builder.Property(p => p.EndDate).IsRequired();

        // FR-3: the planned checkpoint dates that drive the reminder sweep. Mapped as a primitive collection
        // (PostgreSQL date[] via Npgsql; EF's primitive-collection support also works on the InMemory provider).
        builder.PrimitiveCollection(p => p.ScheduledCheckpointDates);

        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.EscalationAction)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(p => p.InitiatedAt);

        builder.Property(p => p.AcknowledgementStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();
        builder.Property(p => p.AcknowledgedAt);

        builder.Property(p => p.OutcomeSetAt);
        builder.Property(p => p.FinalOutcomeNotes).HasMaxLength(4000);

        builder.Property(p => p.EscalationConfirmed).HasDefaultValue(false).IsRequired();
        builder.Property(p => p.EscalationConfirmedAt);
        // SENSITIVE (NFR-4 encryption seam — stored plain text today; see Pip xmldoc).
        builder.Property(p => p.EscalationNotes).HasMaxLength(4000);

        builder.Property(p => p.IsDeleted).HasDefaultValue(false).IsRequired();

        // Optimistic concurrency: maps to the PostgreSQL xmin system column (no schema DDL). Mirrors
        // ManagerReview.Version. The InMemory test provider ignores this annotation.
        builder.Property(p => p.Version).IsRowVersion();

        // BR-2: at most one NON-TERMINAL PIP per employee per tenant. Terminal statuses
        // (SuccessfullyCompleted / NotMet / Cancelled) are excluded so a new PIP can follow a closed one.
        // The string literals match the HasConversion<string>() storage for PipStatus.
        builder.HasIndex(p => new { p.TenantId, p.EmployeeId })
            .IsUnique()
            .HasFilter("is_deleted = false AND status IN ('Draft', 'Active', 'Extended')");

        builder.HasMany(p => p.Objectives)
            .WithOne(o => o.Pip!)
            .HasForeignKey(o => o.PipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Checkpoints)
            .WithOne(c => c.Pip!)
            .HasForeignKey(c => c.PipId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.Events)
            .WithOne(e => e.Pip!)
            .HasForeignKey(e => e.PipId)
            .OnDelete(DeleteBehavior.Cascade);

        // FKs to Employee / ManagerReview are stored as UUID columns without hard FK constraints, mirroring the
        // codebase pattern for cross-module references (tenant-scoped existence validated in the service).
    }
}
