using HRM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRM.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the CompulsoryLeave entity (US-LV-011 FR-6, §7).
/// Maps to the "compulsory_leave" table with snake_case naming convention.
/// </summary>
public sealed class CompulsoryLeaveConfiguration : IEntityTypeConfiguration<CompulsoryLeave>
{
    public void Configure(EntityTypeBuilder<CompulsoryLeave> builder)
    {
        builder.ToTable("compulsory_leave");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id");

        builder.Property(c => c.Date)
            .HasColumnType("date")
            .IsRequired();

        builder.Property(c => c.LeaveTypeId)
            .IsRequired();

        builder.Property(c => c.Reason)
            .HasColumnType("text");

        builder.Property(c => c.AssignedCount)
            .IsRequired();

        builder.Property(c => c.LopCount)
            .IsRequired();

        builder.Property(c => c.IsDeleted)
            .HasDefaultValue(false)
            .IsRequired();

        builder.HasOne(c => c.LeaveType)
            .WithMany()
            .HasForeignKey(c => c.LeaveTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => new { c.TenantId, c.Date })
            .HasDatabaseName("ix_compulsory_leave_tenant_date");
    }
}
