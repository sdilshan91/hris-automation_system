using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="TenantScheduledJob"/> (US-ADM-004). Cross-tenant/system table
/// "tenant_scheduled_jobs" — NOT tenant-scoped (no global query filter): the System Admin schedules/cancels
/// termination jobs across tenants while operating in the admin context.
/// </summary>
public sealed class TenantScheduledJobConfiguration : IEntityTypeConfiguration<TenantScheduledJob>
{
    public void Configure(EntityTypeBuilder<TenantScheduledJob> builder)
    {
        builder.ToTable("tenant_scheduled_jobs");

        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id)
            .HasColumnName("id");

        builder.Property(j => j.TenantId)
            .IsRequired();

        builder.HasIndex(j => j.TenantId)
            .HasDatabaseName("ix_tenant_scheduled_jobs_tenant_id");

        builder.Property(j => j.JobId)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(j => j.JobType)
            .HasMaxLength(32)
            .IsRequired();
    }
}
