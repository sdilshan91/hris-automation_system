// ============================================================================
// US-ADM-011c (Part 1) AC-6/FR-9 — step DELEGATION at activation (snapshot-at-activation, Q3).
//
// When a step has DelegationEnabled + a backup and its PRIMARY approver is on an APPROVED leave spanning
// today, the runtime routes the Pending step-instance to the backup (recorded as a reassignment on
// AssignedApproverUserId + a "workflow.step.delegated" audit row — NOT by flipping Decision). With no
// backup configured the primary is kept (the Tenant Admin is notified). A primary NOT on leave keeps the
// primary. Delegation is evaluated ONCE, at activation.
//
// HARNESS = real Postgres via Testcontainers (mirrors WorkflowRuntimeParallelPostgresTests): the runtime
// touches the leave overlap query + audit rows; MigrateAsync applies the real schema.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class WorkflowDelegationPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var tc = new MutableTenantContext { TenantId = Guid.NewGuid() };
        await using var db = Db(tc, User(Guid.NewGuid()));
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private static ICurrentUser User(Guid userId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("u@acme.com");
        return u;
    }

    private AppDbContext Db(ITenantContext tc, ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(user))
            .Options, tc);

    private WorkflowRuntimeService Runtime(AppDbContext db, ITenantContext tc, ICurrentUser user) =>
        new(db, tc, user, NullLogger<WorkflowRuntimeService>.Instance);

    /// <summary>Seeds a tenant + a definition (Leave, Active) with one delegation-enabled NamedUser step, plus the
    /// primary approver's employee and (optionally) an approved leave spanning today. Returns the ids.</summary>
    private async Task<(Guid tenantId, Guid definitionId, Guid primaryUser, Guid backupUser)> SeedAsync(
        bool primaryOnLeave, bool withBackup)
    {
        var tenantId = Guid.NewGuid();
        var primaryUser = Guid.NewGuid();
        var backupUser = Guid.NewGuid();
        var tc = new MutableTenantContext { TenantId = tenantId };

        await using var db = Db(tc, User(primaryUser));

        db.Tenants.Add(new Tenant { Id = tenantId, Subdomain = "acme", Name = "Acme" });
        db.Set<User>().Add(new User { Id = primaryUser, Email = "primary@acme.com", IsActive = true });

        var deptId = BaseEntity.NewUuidV7();
        var jobId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = "Ops", Code = "OPS", IsActive = true });
        db.JobTitles.Add(new JobTitle { Id = jobId, TenantId = tenantId, TitleName = "Lead", IsActive = true });

        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, UserId = primaryUser, EmployeeNo = "EMP-P",
            FirstName = "Pat", LastName = "Primary", Email = "primary@acme.com",
            DateOfJoining = new DateTime(2022, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            DepartmentId = deptId, JobTitleId = jobId, EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active, IsActive = true,
        });

        var leaveTypeId = BaseEntity.NewUuidV7();
        db.LeaveTypes.Add(new LeaveType { Id = leaveTypeId, TenantId = tenantId, Name = "Annual", AnnualEntitlement = 20m });

        if (primaryOnLeave)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            db.LeaveRequests.Add(new LeaveRequest
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = empId, LeaveTypeId = leaveTypeId,
                StartDate = today.AddDays(-1), EndDate = today.AddDays(1), TotalDays = 3m,
                Status = LeaveRequestStatus.Approved, RequestedAt = DateTime.UtcNow, AttachmentUrls = [],
            });
        }

        var definitionId = BaseEntity.NewUuidV7();
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Id = definitionId, TenantId = tenantId, Name = "Leave Approval",
            EntityType = WorkflowEntityType.Leave, LineageId = definitionId, Version = 1,
            Status = WorkflowStatus.Active, IsActive = true,
        });
        db.WorkflowSteps.Add(new WorkflowStep
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, WorkflowDefinitionId = definitionId,
            StepOrder = 1, ApproverType = WorkflowApproverType.NamedUser, ApproverIdentifier = primaryUser,
            IsParallel = false, SlaHours = 24,
            DelegationEnabled = true, DelegationBackupUserId = withBackup ? backupUser : null,
        });

        await db.SaveChangesAsync();
        return (tenantId, definitionId, primaryUser, backupUser);
    }

    // ── AC-6: primary on leave + backup configured → the step is assigned to the backup + audit row ──
    [Fact]
    public async Task PrimaryOnLeave_WithBackup_StepAssignedToBackup()
    {
        var (tenantId, _, primaryUser, backupUser) = await SeedAsync(primaryOnLeave: true, withBackup: true);
        var tc = new MutableTenantContext { TenantId = tenantId };
        var entityId = Guid.NewGuid();

        await using (var db = Db(tc, User(Guid.NewGuid())))
        {
            var created = await Runtime(db, tc, User(Guid.NewGuid()))
                .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, entityId, null,
                    new Dictionary<string, object?> { ["days_requested"] = 3m });
            created.InstanceCreated.Should().BeTrue();
        }

        await using var verify = Db(tc, User(Guid.NewGuid()));
        var step = await verify.WorkflowStepInstances.AsNoTracking()
            .FirstAsync(s => s.StepOrder == 1 && s.Decision == WorkflowStepDecision.Pending);
        step.AssignedApproverUserId.Should().Be(backupUser, "delegation routes the primary's step to the backup");

        (await verify.AuditLogs.AsNoTracking().CountAsync(a => a.EventType == "workflow.step.delegated"))
            .Should().BeGreaterThan(0, "a delegation is audited");
    }

    // ── AC-6: primary on leave, NO backup → the primary is kept (Tenant Admin notified) ──
    [Fact]
    public async Task PrimaryOnLeave_NoBackup_StepKeptWithPrimary()
    {
        var (tenantId, _, primaryUser, _) = await SeedAsync(primaryOnLeave: true, withBackup: false);
        var tc = new MutableTenantContext { TenantId = tenantId };
        var entityId = Guid.NewGuid();

        await using (var db = Db(tc, User(Guid.NewGuid())))
            await Runtime(db, tc, User(Guid.NewGuid()))
                .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, entityId, null,
                    new Dictionary<string, object?> { ["days_requested"] = 3m });

        await using var verify = Db(tc, User(Guid.NewGuid()));
        var step = await verify.WorkflowStepInstances.AsNoTracking()
            .FirstAsync(s => s.StepOrder == 1 && s.Decision == WorkflowStepDecision.Pending);
        step.AssignedApproverUserId.Should().Be(primaryUser, "with no backup the primary keeps the step");
    }

    // ── Primary NOT on leave → no delegation, the primary keeps the step ──
    [Fact]
    public async Task PrimaryNotOnLeave_StepKeptWithPrimary()
    {
        var (tenantId, _, primaryUser, _) = await SeedAsync(primaryOnLeave: false, withBackup: true);
        var tc = new MutableTenantContext { TenantId = tenantId };
        var entityId = Guid.NewGuid();

        await using (var db = Db(tc, User(Guid.NewGuid())))
            await Runtime(db, tc, User(Guid.NewGuid()))
                .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, entityId, null,
                    new Dictionary<string, object?> { ["days_requested"] = 3m });

        await using var verify = Db(tc, User(Guid.NewGuid()));
        var step = await verify.WorkflowStepInstances.AsNoTracking()
            .FirstAsync(s => s.StepOrder == 1 && s.Decision == WorkflowStepDecision.Pending);
        step.AssignedApproverUserId.Should().Be(primaryUser);

        (await verify.AuditLogs.AsNoTracking().CountAsync(a => a.EventType == "workflow.step.delegated"))
            .Should().Be(0, "no delegation should fire when the primary is not on leave");
    }
}
