// ============================================================================
// BUG-309 + BUG-310 — the two defects that made the C1 workflow seed unshippable.
//
// WHY THEY WERE INVISIBLE. Both live on the workflow-driven leave path, and until the C1 seed NO tenant
// had a workflow definition — so that whole path was dead code in practice. The seed would have turned it
// on for every tenant simultaneously, converting two dormant defects into universal ones. They are fixed
// FIRST, and this suite is what stops them coming back.
//
//   BUG-309  Approve/reject notifications were passed _currentUser.UserId in a parameter named
//            approverEmployeeId. Legacy passes manager.Id (an employee id). The method was inconsistent
//            with ITSELF: StageLeaveApprovalAsync resolves an employee id for the history row a few lines
//            away. A wrong identity TYPE, which no compiler catches because both are Guid.
//
//   BUG-310  When LineManager resolved to nothing, an instance was still created with no approver.
//            Nobody could decide it, and there was no legacy escape because WorkflowInstanceId was set.
//            The regression is the SNAPSHOT: legacy left the request plain-pending and it became
//            approvable as soon as a manager was assigned; the engine resolves once at activation and
//            never re-resolves.
//
// Postgres, not InMemory: Employee.UserId is a real nullable FK and the relationships under test
// (requester -> manager -> user) are exactly what InMemory declines to enforce.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
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

public sealed class WorkflowLeaveDecisionDefectsPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _managerEmployeeId = BaseEntity.NewUuidV7();
    private readonly Guid _requesterEmployeeId = BaseEntity.NewUuidV7();
    private readonly Guid _requesterUserId = Guid.NewGuid();

    // A requester whose manager exists as an EMPLOYEE but has no login (Employee.UserId is Guid?).
    private readonly Guid _orphanRequesterEmployeeId = BaseEntity.NewUuidV7();
    private readonly Guid _loginlessManagerEmployeeId = BaseEntity.NewUuidV7();

    // A requester with no manager at all.
    private readonly Guid _managerlessEmployeeId = BaseEntity.NewUuidV7();

    private Guid _definitionId;
    private readonly Guid _leaveTypeId = BaseEntity.NewUuidV7();
    private readonly MutableTenantContext _tc = new();

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

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tc.SetTenant(_tenantId, "acme", TenantStatus.Active);

        await using var db = Db(User(_requesterUserId));
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });

        var departmentId = BaseEntity.NewUuidV7();
        var jobTitleId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = departmentId, TenantId = _tenantId, Name = "Ops", Code = "OPS" });
        db.JobTitles.Add(new JobTitle { Id = jobTitleId, TenantId = _tenantId, TitleName = "Analyst" });

        db.Users.Add(new User { Id = _managerUserId, Email = "mia@acme.com", DisplayName = "Mia Manager" });
        db.Users.Add(new User { Id = _requesterUserId, Email = "raj@acme.com", DisplayName = "Raj Requester" });

        Employee Emp(Guid id, string no, Guid? userId, Guid? reportsTo) => new()
        {
            Id = id,
            TenantId = _tenantId,
            UserId = userId,
            EmployeeNo = no,
            FirstName = no,
            LastName = "Test",
            Email = $"{no}@acme.com".ToLowerInvariant(),
            DateOfJoining = new DateTime(2024, 1, 1),
            DepartmentId = departmentId,
            JobTitleId = jobTitleId,
            ReportsToEmployeeId = reportsTo,
        };

        db.Employees.Add(Emp(_managerEmployeeId, "MGR1", _managerUserId, null));
        db.Employees.Add(Emp(_requesterEmployeeId, "EMP1", _requesterUserId, _managerEmployeeId));
        // Manager row with NO user account — the case that used to strand a request permanently.
        db.Employees.Add(Emp(_loginlessManagerEmployeeId, "MGR2", null, null));
        db.Employees.Add(Emp(_orphanRequesterEmployeeId, "EMP2", null, _loginlessManagerEmployeeId));
        db.Employees.Add(Emp(_managerlessEmployeeId, "EMP3", null, null));

        _definitionId = BaseEntity.NewUuidV7();
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Id = _definitionId,
            TenantId = _tenantId,
            Name = "Default Leave Approval",
            EntityType = WorkflowEntityType.Leave,
            LineageId = _definitionId,
            Version = 1,
            Status = WorkflowStatus.Active,
            IsActive = true,
            IsDefault = true,
            Steps =
            [
                new WorkflowStep
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantId,
                    WorkflowDefinitionId = _definitionId,
                    StepOrder = 1,
                    ApproverType = WorkflowApproverType.LineManager,
                    SlaHours = 0,
                },
            ],
        });

        // NegativeBalanceAllowed keeps this focused on BUG-309 (which id is notified) instead of dragging in
        // entitlement/accrual seeding that has nothing to do with the defect.
        db.LeaveTypes.Add(new LeaveType
        {
            Id = _leaveTypeId, TenantId = _tenantId, Name = "Annual",
            NegativeBalanceAllowed = true, NegativeBalanceLimit = null,
        });

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static ICurrentUser User(Guid userId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("user@acme.com");
        return u;
    }

    private AppDbContext Db(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(_tc), new AuditInterceptor(user))
            .Options, _tc);

    private WorkflowRuntimeService Runtime(AppDbContext db, ICurrentUser user) =>
        new(db, _tc, user, NullLogger<WorkflowRuntimeService>.Instance);

    private static Dictionary<string, object?> Data() => new() { ["days_requested"] = 2m };

    // ── BUG-310 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// THE ARM THAT MATTERS FOR BUG-310. A requester whose manager has no login must fall back to the legacy
    /// path — NOT get an instance assigned to nobody. Creating one is worse than doing nothing, because the
    /// approver is snapshotted at activation and giving the manager a login later does not re-resolve it.
    /// </summary>
    [Fact]
    public async Task Submit_WhenLineManagerHasNoUserAccount_FallsBackToLegacy_NotAStuckInstance_BUG310()
    {
        await using var db = Db(User(_requesterUserId));

        var result = await Runtime(db, User(_requesterUserId)).CreateInstanceOnSubmitAsync(
            WorkflowEntityType.Leave, Guid.NewGuid(), _orphanRequesterEmployeeId, Data());

        result.InstanceCreated.Should().BeFalse(
            "Employee.UserId is nullable, so a manager can exist without a login. Creating an instance whose "
            + "only approver resolves to null strands the request permanently — the engine snapshots the "
            + "approver at activation and never re-resolves, so it cannot self-heal the way legacy did");
    }

    /// <summary>
    /// Same guarantee for a requester with no manager at all.
    /// </summary>
    [Fact]
    public async Task Submit_WhenRequesterHasNoManager_FallsBackToLegacy_BUG310()
    {
        await using var db = Db(User(_requesterUserId));

        var result = await Runtime(db, User(_requesterUserId)).CreateInstanceOnSubmitAsync(
            WorkflowEntityType.Leave, Guid.NewGuid(), _managerlessEmployeeId, Data());

        result.InstanceCreated.Should().BeFalse(
            "with no ReportsToEmployeeId there is nobody for LineManager to resolve to");
    }

    /// <summary>
    /// The guard must not over-trigger: a resolvable manager still routes through the engine. Without this,
    /// "always return Legacy()" would pass both arms above and silently disable the whole feature.
    /// </summary>
    [Fact]
    public async Task Submit_WithAResolvableLineManager_StillCreatesTheInstance_BUG310()
    {
        await using var db = Db(User(_requesterUserId));

        var result = await Runtime(db, User(_requesterUserId)).CreateInstanceOnSubmitAsync(
            WorkflowEntityType.Leave, Guid.NewGuid(), _requesterEmployeeId, Data());

        result.InstanceCreated.Should().BeTrue(
            "the fallback must fire ONLY when no approver is reachable — otherwise it disables the engine");
    }

    /// <summary>
    /// A rejected submission must leave NO trace. If the instance row were written and only the result said
    /// "legacy", the request would still be picked up as workflow-driven later.
    /// </summary>
    [Fact]
    public async Task Submit_WhenNoApproverIsReachable_WritesNoInstanceRow_BUG310()
    {
        var entityId = Guid.NewGuid();
        await using (var db = Db(User(_requesterUserId)))
        {
            await Runtime(db, User(_requesterUserId)).CreateInstanceOnSubmitAsync(
                WorkflowEntityType.Leave, entityId, _orphanRequesterEmployeeId, Data());
        }

        await using (var verify = Db(User(_requesterUserId)))
        {
            (await verify.WorkflowInstances.AsNoTracking().AnyAsync(i => i.EntityId == entityId))
                .Should().BeFalse("a half-created instance would still make the request workflow-driven");
            (await verify.WorkflowStepInstances.AsNoTracking().AnyAsync())
                .Should().BeFalse("no orphan step rows either");
        }
    }

    // ── BUG-309 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// THE ARM THAT MATTERS FOR BUG-309. `NotifyLeaveApprovedAsync`'s third parameter is
    /// <c>approverEmployeeId</c>. The workflow path used to pass <c>_currentUser.UserId</c> — a USER id —
    /// into it, while the legacy path passes <c>manager.Id</c>, an EMPLOYEE id.
    ///
    /// Nothing could catch this by type: both are <see cref="Guid"/>. It is caught here by asserting the
    /// notified value is the manager's EMPLOYEE id AND explicitly is NOT their USER id — the two are
    /// different Guids in this fixture precisely so the confusion is observable.
    /// </summary>
    [Fact]
    public async Task WorkflowApproval_NotifiesTheApproversEMPLOYEEId_NotTheirUserId_BUG309()
    {
        var notifications = Substitute.For<ILeaveNotificationService>();
        var leaveRequestId = BaseEntity.NewUuidV7();

        // Submit as the requester: creates the request and its workflow instance.
        await using (var db = Db(User(_requesterUserId)))
        {
            db.LeaveRequests.Add(new LeaveRequest
            {
                Id = leaveRequestId,
                TenantId = _tenantId,
                EmployeeId = _requesterEmployeeId,
                LeaveTypeId = _leaveTypeId,
                StartDate = new DateOnly(2026, 9, 1),
                EndDate = new DateOnly(2026, 9, 2),
                TotalDays = 2m,
                Status = LeaveRequestStatus.Pending,
            });
            await db.SaveChangesAsync();

            var created = await Runtime(db, User(_requesterUserId)).CreateInstanceOnSubmitAsync(
                WorkflowEntityType.Leave, leaveRequestId, _requesterEmployeeId, Data());
            created.InstanceCreated.Should().BeTrue();

            var lr = await db.LeaveRequests.FirstAsync(r => r.Id == leaveRequestId);
            lr.WorkflowInstanceId = created.InstanceId;
            await db.SaveChangesAsync();
        }

        // Approve as the MANAGER — the acting user whose identity the notification records.
        await using (var db = Db(User(_managerUserId)))
        {
            var service = new LeaveRequestService(
                db, _tc, User(_managerUserId),
                new NoOpHolidayProvider(),
                notifications,
                NullLogger<LeaveRequestService>.Instance,
                new TenantLeaveYearResolver(db, _tc),
                holidayService: null,
                leaveTypeService: null,
                workflowRuntime: Runtime(db, User(_managerUserId)));

            var result = await service.ApproveAsync(leaveRequestId, "ok");
            result.IsSuccess.Should().BeTrue(result.Error);
        }

        await notifications.Received(1).NotifyLeaveApprovedAsync(
            leaveRequestId,
            _requesterEmployeeId,
            _managerEmployeeId,
            Arg.Any<CancellationToken>());

        await notifications.DidNotReceive().NotifyLeaveApprovedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), _managerUserId, Arg.Any<CancellationToken>());
    }


    // ── ISSUE-387 ────────────────────────────────────────────────────────────

    /// <summary>
    /// ISSUE-387: a workflow-driven approval must still write the SEMANTIC <c>Leave.Approved</c> audit row.
    ///
    /// The engine already writes <c>workflow.instance.approved</c> with ResourceType "WorkflowInstance".
    /// That records a workflow STEP being approved; ISSUE-037/FR-7 is about a LEAVE REQUEST being approved
    /// and requires the trail to be queryable BY ACTION. Before C1 every tenant used the legacy path so that
    /// query was complete — C1 made the engine live for everyone, which would have silently dropped every
    /// workflow-driven decision out of it.
    ///
    /// Asserts the ROW EXISTS IN THE DATABASE, not that a method was called: the staging methods do not save,
    /// so the row only survives if the workflow runtime's commit actually carries it.
    /// </summary>
    [Fact]
    public async Task WorkflowApproval_WritesTheSemanticLeaveApprovedAuditRow_ISSUE387()
    {
        var notifications = Substitute.For<ILeaveNotificationService>();
        var leaveRequestId = BaseEntity.NewUuidV7();

        await using (var db = Db(User(_requesterUserId)))
        {
            db.LeaveRequests.Add(new LeaveRequest
            {
                Id = leaveRequestId,
                TenantId = _tenantId,
                EmployeeId = _requesterEmployeeId,
                LeaveTypeId = _leaveTypeId,
                StartDate = new DateOnly(2026, 10, 1),
                EndDate = new DateOnly(2026, 10, 2),
                TotalDays = 2m,
                Status = LeaveRequestStatus.Pending,
            });
            await db.SaveChangesAsync();

            var created = await Runtime(db, User(_requesterUserId)).CreateInstanceOnSubmitAsync(
                WorkflowEntityType.Leave, leaveRequestId, _requesterEmployeeId, Data());
            created.InstanceCreated.Should().BeTrue();

            var lr = await db.LeaveRequests.FirstAsync(r => r.Id == leaveRequestId);
            lr.WorkflowInstanceId = created.InstanceId;
            await db.SaveChangesAsync();
        }

        await using (var db = Db(User(_managerUserId)))
        {
            var service = new LeaveRequestService(
                db, _tc, User(_managerUserId),
                new NoOpHolidayProvider(),
                notifications,
                NullLogger<LeaveRequestService>.Instance,
                new TenantLeaveYearResolver(db, _tc),
                holidayService: null,
                leaveTypeService: null,
                workflowRuntime: Runtime(db, User(_managerUserId)));

            (await service.ApproveAsync(leaveRequestId, "ok")).IsSuccess.Should().BeTrue();
        }

        await using (var verify = Db(User(_managerUserId)))
        {
            var semantic = await verify.AuditLogs.IgnoreQueryFilters().AsNoTracking()
                .Where(a => a.Action == "Leave.Approved"
                            && a.ResourceType == "LeaveRequest"
                            && a.ResourceId == leaveRequestId.ToString())
                .ToListAsync();

            semantic.Should().ContainSingle(
                "an auditor filtering the leave trail BY ACTION must find workflow-driven approvals too — "
                + "workflow.instance.approved records a workflow step, not a leave request");
        }
    }
}
