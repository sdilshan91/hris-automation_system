using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext with multi-tenant global query filters.
/// All tenant-scoped entities are filtered by the current tenant context.
/// </summary>
public sealed class AppDbContext : DbContext, IUnitOfWork
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserTenantRole> UserTenantRoles => Set<UserTenantRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<JobTitle> JobTitles => Set<JobTitle>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();
    public DbSet<EmploymentHistory> EmploymentHistories => Set<EmploymentHistory>();
    public DbSet<EmployeeFieldAuditLog> EmployeeFieldAuditLogs => Set<EmployeeFieldAuditLog>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<EmployeeDocument> EmployeeDocuments => Set<EmployeeDocument>();
    public DbSet<FutureDatedStatusChange> FutureDatedStatusChanges => Set<FutureDatedStatusChange>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<BulkImportJob> BulkImportJobs => Set<BulkImportJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all entity configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global query filters for tenant isolation
        modelBuilder.Entity<UserTenant>()
            .HasQueryFilter(ut => !_tenantContext.IsResolved || ut.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Role>()
            .HasQueryFilter(r => r.TenantId == null || !_tenantContext.IsResolved || r.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<RefreshToken>()
            .HasQueryFilter(rt => !_tenantContext.IsResolved || rt.TenantId == _tenantContext.TenantId);

        modelBuilder.Entity<Tenant>()
            .HasQueryFilter(t => !t.IsDeleted);

        // US-CHR-004: Department tenant isolation + soft-delete filter
        modelBuilder.Entity<Department>()
            .HasQueryFilter(d => !d.IsDeleted && (!_tenantContext.IsResolved || d.TenantId == _tenantContext.TenantId));

        // US-CHR-005: JobTitle tenant isolation + soft-delete filter
        modelBuilder.Entity<JobTitle>()
            .HasQueryFilter(j => !j.IsDeleted && (!_tenantContext.IsResolved || j.TenantId == _tenantContext.TenantId));

        // US-CHR-001: Employee tenant isolation + soft-delete filter
        modelBuilder.Entity<Employee>()
            .HasQueryFilter(e => !e.IsDeleted && (!_tenantContext.IsResolved || e.TenantId == _tenantContext.TenantId));

        // US-CHR-002: EmergencyContact tenant isolation + soft-delete filter
        modelBuilder.Entity<EmergencyContact>()
            .HasQueryFilter(ec => !ec.IsDeleted && (!_tenantContext.IsResolved || ec.TenantId == _tenantContext.TenantId));

        // US-CHR-002: EmploymentHistory tenant isolation + soft-delete filter
        modelBuilder.Entity<EmploymentHistory>()
            .HasQueryFilter(eh => !eh.IsDeleted && (!_tenantContext.IsResolved || eh.TenantId == _tenantContext.TenantId));

        // US-CHR-007: Location tenant isolation + soft-delete filter
        modelBuilder.Entity<Location>()
            .HasQueryFilter(l => !l.IsDeleted && (!_tenantContext.IsResolved || l.TenantId == _tenantContext.TenantId));

        // US-CHR-008: EmployeeDocument tenant isolation + soft-delete filter
        modelBuilder.Entity<EmployeeDocument>()
            .HasQueryFilter(d => !d.IsDeleted && (!_tenantContext.IsResolved || d.TenantId == _tenantContext.TenantId));

        // US-CHR-009: FutureDatedStatusChange tenant isolation + soft-delete filter
        modelBuilder.Entity<FutureDatedStatusChange>()
            .HasQueryFilter(f => !f.IsDeleted && (!_tenantContext.IsResolved || f.TenantId == _tenantContext.TenantId));

        // US-CHR-009: IdempotencyRecord — no soft-delete; tenant isolation only
        modelBuilder.Entity<IdempotencyRecord>()
            .HasQueryFilter(i => !_tenantContext.IsResolved || i.TenantId == _tenantContext.TenantId);

        // US-CHR-010: BulkImportJob tenant isolation + soft-delete filter
        modelBuilder.Entity<BulkImportJob>()
            .HasQueryFilter(b => !b.IsDeleted && (!_tenantContext.IsResolved || b.TenantId == _tenantContext.TenantId));
    }
}
