using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the PayrollRun entity (US-PAY-003 FR-1). Maps to the "payroll_run" table with
/// snake_case naming. Tenant isolation via the global query filter + TenantInterceptor (Postgres RLS is the
/// deferred US-PLT-002). Enforces BR-1 (one non-cancelled run per period) and FR-9 (idempotency-key unique
/// per tenant) via partial unique indexes.
/// </summary>
public sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> builder)
    {
        builder.ToTable("payroll_run");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.PayMonth).IsRequired();
        builder.Property(x => x.PayYear).IsRequired();

        // Status stored as varchar(20) (FR-1) — matches the SalaryComponentType.HasConversion<string>() pattern.
        // ENH-021: read through the tolerant converter so a row with a status string outside the enum maps to
        // the Unknown sentinel (and is logged) instead of 500ing the run list/guard query on materialization.
        builder.Property(x => x.Status)
            .HasConversion(new TolerantEnumToStringConverter<PayrollRunStatus>(PayrollRunStatus.Unknown))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.TotalEmployees).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.ProcessedEmployees).HasDefaultValue(0).IsRequired();
        builder.Property(x => x.SkippedEmployees).HasDefaultValue(0).IsRequired();

        builder.Property(x => x.TotalGross).HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.TotalDeductions).HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.TotalNet).HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();
        builder.Property(x => x.TotalStatutory).HasColumnType("numeric(18,2)").HasDefaultValue(0m).IsRequired();

        builder.Property(x => x.InitiatedBy).IsRequired();
        builder.Property(x => x.InitiatedAt).IsRequired();
        builder.Property(x => x.CompletedAt);

        // US-PAY-008 approval-workflow state (additive).
        builder.Property(x => x.CurrentWorkflowInstanceId);
        builder.Property(x => x.CurrentApprovalStep);
        builder.Property(x => x.TotalApprovalSteps);
        builder.Property(x => x.SubmittedBy);
        builder.Property(x => x.SubmittedAt);
        builder.Property(x => x.RejectionReason).HasColumnType("text");

        // US-PAY-008 FR-3 (ISSUE-173): per-step SLA due-at + escalated-at (both nullable, opt-in).
        builder.Property(x => x.SlaDueAt);
        builder.Property(x => x.EscalatedAt);

        builder.Property(x => x.ApprovedBy);
        builder.Property(x => x.ApprovedAt);
        builder.Property(x => x.FinalizedAt);

        builder.Property(x => x.IdempotencyKey).HasMaxLength(100);
        builder.Property(x => x.RunLog).HasColumnType("text");

        builder.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

        // List/read by tenant + period.
        builder.HasIndex(x => new { x.TenantId, x.PayYear, x.PayMonth });

        // BR-1: at most ONE non-cancelled run per (tenant, pay_year, pay_month). Partial unique index
        // excludes Cancelled runs (and soft-deleted). This is the documented locking choice (NFR-3) in the
        // absence of a distributed-lock infra — a duplicate initiate fails fast at the DB level.
        builder.HasIndex(x => new { x.TenantId, x.PayYear, x.PayMonth })
            .IsUnique()
            .HasFilter("status <> 'Cancelled' AND is_deleted = false")
            .HasDatabaseName("ix_payroll_run_one_active_per_period");

        // FR-9: idempotency key unique per tenant (partial — only rows that supplied a key).
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL AND is_deleted = false")
            .HasDatabaseName("ix_payroll_run_idempotency_key_per_tenant");
    }
}
