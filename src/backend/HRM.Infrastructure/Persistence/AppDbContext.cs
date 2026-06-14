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
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<LeaveEntitlementRule> LeaveEntitlementRules => Set<LeaveEntitlementRule>();
    public DbSet<LeaveEntitlementOverride> LeaveEntitlementOverrides => Set<LeaveEntitlementOverride>();
    public DbSet<LeaveLedger> LeaveLedgerEntries => Set<LeaveLedger>();
    public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<LeaveApprovalHistory> LeaveApprovalHistories => Set<LeaveApprovalHistory>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<LeaveCarryForwardTracking> LeaveCarryForwardTrackings => Set<LeaveCarryForwardTracking>();
    public DbSet<CompulsoryLeave> CompulsoryLeaves => Set<CompulsoryLeave>();
    public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();
    public DbSet<AttendanceSettings> AttendanceSettings => Set<AttendanceSettings>();
    public DbSet<AttendanceRegularization> AttendanceRegularizations => Set<AttendanceRegularization>();
    public DbSet<RegularizationApprovalHistory> RegularizationApprovalHistories => Set<RegularizationApprovalHistory>();
    public DbSet<PayrollLockPeriod> PayrollLockPeriods => Set<PayrollLockPeriod>();

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

        // US-CHR-012: CustomFieldDefinition tenant isolation + soft-delete filter
        modelBuilder.Entity<CustomFieldDefinition>()
            .HasQueryFilter(c => !c.IsDeleted && (!_tenantContext.IsResolved || c.TenantId == _tenantContext.TenantId));

        // US-LV-001: LeaveType tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveType>()
            .HasQueryFilter(lt => !lt.IsDeleted && (!_tenantContext.IsResolved || lt.TenantId == _tenantContext.TenantId));

        // US-LV-002: LeaveEntitlementRule tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveEntitlementRule>()
            .HasQueryFilter(r => !r.IsDeleted && (!_tenantContext.IsResolved || r.TenantId == _tenantContext.TenantId));

        // US-LV-002: LeaveEntitlementOverride tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveEntitlementOverride>()
            .HasQueryFilter(o => !o.IsDeleted && (!_tenantContext.IsResolved || o.TenantId == _tenantContext.TenantId));

        // US-LV-002: LeaveLedger tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveLedger>()
            .HasQueryFilter(l => !l.IsDeleted && (!_tenantContext.IsResolved || l.TenantId == _tenantContext.TenantId));

        // US-LV-003: LeaveRequest tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveRequest>()
            .HasQueryFilter(lr => !lr.IsDeleted && (!_tenantContext.IsResolved || lr.TenantId == _tenantContext.TenantId));

        // US-LV-005: LeaveApprovalHistory tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveApprovalHistory>()
            .HasQueryFilter(h => !h.IsDeleted && (!_tenantContext.IsResolved || h.TenantId == _tenantContext.TenantId));

        // US-LV-007: Holiday tenant isolation + soft-delete filter
        modelBuilder.Entity<Holiday>()
            .HasQueryFilter(h => !h.IsDeleted && (!_tenantContext.IsResolved || h.TenantId == _tenantContext.TenantId));

        // US-LV-008: LeaveCarryForwardTracking tenant isolation + soft-delete filter
        modelBuilder.Entity<LeaveCarryForwardTracking>()
            .HasQueryFilter(t => !t.IsDeleted && (!_tenantContext.IsResolved || t.TenantId == _tenantContext.TenantId));

        // US-LV-011: CompulsoryLeave tenant isolation + soft-delete filter
        modelBuilder.Entity<CompulsoryLeave>()
            .HasQueryFilter(c => !c.IsDeleted && (!_tenantContext.IsResolved || c.TenantId == _tenantContext.TenantId));

        // US-ATT-001: AttendanceLog tenant isolation + soft-delete filter
        modelBuilder.Entity<AttendanceLog>()
            .HasQueryFilter(a => !a.IsDeleted && (!_tenantContext.IsResolved || a.TenantId == _tenantContext.TenantId));

        // US-ATT-001: AttendanceSettings tenant isolation + soft-delete filter
        modelBuilder.Entity<AttendanceSettings>()
            .HasQueryFilter(s => !s.IsDeleted && (!_tenantContext.IsResolved || s.TenantId == _tenantContext.TenantId));

        // US-ATT-003: AttendanceRegularization tenant isolation + soft-delete filter
        modelBuilder.Entity<AttendanceRegularization>()
            .HasQueryFilter(r => !r.IsDeleted && (!_tenantContext.IsResolved || r.TenantId == _tenantContext.TenantId));

        // US-ATT-003: PayrollLockPeriod (placeholder) tenant isolation + soft-delete filter
        modelBuilder.Entity<PayrollLockPeriod>()
            .HasQueryFilter(p => !p.IsDeleted && (!_tenantContext.IsResolved || p.TenantId == _tenantContext.TenantId));

        // US-ATT-004: RegularizationApprovalHistory tenant isolation + soft-delete filter
        modelBuilder.Entity<RegularizationApprovalHistory>()
            .HasQueryFilter(h => !h.IsDeleted && (!_tenantContext.IsResolved || h.TenantId == _tenantContext.TenantId));
    }
}
