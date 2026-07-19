// ============================================================================
// ISSUE-099 / DF-18 (US-PRF-001) — GET /api/v1/tenant/performance/goals/{id}.
//
// ISSUE-099 made GetByIdAsync tenant-scoped (foreign-tenant / unknown id -> 404 via the EF
// global query filter, never a misleading empty 200). DF-18 then added WITHIN-tenant read
// authorization: previously any Performance.SetGoal.Team manager could read ANY in-tenant
// goal. GetByIdAsync now runs AuthorizeForEmployeeAsync (BR-4: HR SetGoal.All -> allow; else
// require SetGoal.Team AND the target is a direct report) with a self-read branch first so the
// goal's OWNER (Employee.UserId == caller UserId) is never locked out of their own goal.
//
// Drives the REAL GoalService through the InMemory-through-real-EF harness (goals/employees are
// plain rows; the global query filter is exercised). Mirrors GoalServiceSaveGoalsTests.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class GoalServiceGetByIdTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();   // the goal-owner employee
    private readonly Guid _ownerUserId = Guid.NewGuid();  // the user linked to the owner employee
    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly IPerformanceNotificationService _notifications;

    public GoalServiceGetByIdTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _notifications = Substitute.For<IPerformanceNotificationService>();
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private GoalService Service(ICurrentUser currentUser) => new(
        Db(), _tenantContext, currentUser, _notifications, Substitute.For<ILogger<GoalService>>());

    /// <summary>Builds an ICurrentUser with the given user id + permission set.</summary>
    private static ICurrentUser User(Guid userId, params string[] permissions)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("caller@acme.com");
        u.Permissions.Returns(permissions);
        return u;
    }

    /// <summary>An HR caller: holds Performance.SetGoal.All, not linked to any employee.</summary>
    private ICurrentUser HrUser() => User(Guid.NewGuid(), PermissionCatalog.Performance.SetGoalAll);

    private void SeedTenant()
    {
        using var db = Db();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.SaveChanges();
    }

    /// <summary>Seeds the goal-owner employee (linked to <see cref="_ownerUserId"/>), optionally reporting to a manager.</summary>
    private void SeedOwnerEmployee(Guid? reportsTo = null)
    {
        using var db = Db();
        db.Employees.Add(new Employee
        {
            Id = _employeeId, TenantId = _tenantId, UserId = _ownerUserId,
            EmployeeNo = "EMP-0001", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
            ReportsToEmployeeId = reportsTo,
        });
        db.SaveChanges();
    }

    /// <summary>Seeds a manager employee + user with SetGoal.Team; returns (userId, employeeId).</summary>
    private (Guid userId, Guid empId) SeedManager()
    {
        var managerUserId = Guid.NewGuid();
        var managerEmpId = Guid.NewGuid();
        using var db = Db();
        db.Employees.Add(new Employee
        {
            Id = managerEmpId, TenantId = _tenantId, UserId = managerUserId,
            EmployeeNo = "EMP-9000", FirstName = "Grace", LastName = "Hopper",
            Email = "grace@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
        });
        db.SaveChanges();
        return (managerUserId, managerEmpId);
    }

    private Guid SeedGoal(Guid tenantId)
    {
        using var db = Db();
        var id = BaseEntity.NewUuidV7();
        db.Goals.Add(new Goal
        {
            Id = id, TenantId = tenantId, CycleId = _cycleId, EmployeeId = _employeeId,
            Title = "Ship it", Description = "desc", Category = GoalCategory.Kpi, Weight = 40,
            TargetValue = "100%", MeasurementUnit = "%",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            Status = GoalStatus.Draft, IsDeleted = false,
        });
        db.SaveChanges();
        return id;
    }

    [Fact]
    [Trait("TC", "TC-PRF-001-14")]
    public async Task GetById_HrWithSetGoalAll_Returns200WithDto()
    {
        SeedTenant();
        SeedOwnerEmployee();
        var goalId = SeedGoal(_tenantId);

        var result = await Service(HrUser()).GetByIdAsync(goalId);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Id.Should().Be(goalId);
        result.Value.Title.Should().Be("Ship it");
        result.Value.Weight.Should().Be(40);
    }

    [Fact]
    [Trait("TC", "TC-PRF-001-14")]
    public async Task GetById_UnknownId_Returns404GoalNotFound()
    {
        SeedTenant();
        SeedOwnerEmployee();
        SeedGoal(_tenantId); // some other goal exists

        var result = await Service(HrUser()).GetByIdAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("goal_not_found");
    }

    [Fact]
    [Trait("TC", "TC-PRF-001-14")]
    public async Task GetById_ForeignTenantGoal_IsInvisible_Returns404()
    {
        // A goal owned by ANOTHER tenant is filtered out by the global query filter (keyed off the
        // resolved tenant), so it is indistinguishable from a non-existent id -> 404, never leaked.
        SeedTenant();
        SeedOwnerEmployee();
        var foreignGoalId = SeedGoal(Guid.NewGuid());

        var result = await Service(HrUser()).GetByIdAsync(foreignGoalId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("goal_not_found");
    }

    // ── DF-18: within-tenant read authorization ───────────────────────

    [Fact]
    [Trait("TC", "TC-PRF-001-14")]
    public async Task GetById_TeamManager_ReadingNonReportsGoal_Denied403()
    {
        SeedTenant();
        var (managerUserId, _) = SeedManager();
        // The goal owner reports to SOMEBODY ELSE, not this manager.
        SeedOwnerEmployee(reportsTo: Guid.NewGuid());
        var goalId = SeedGoal(_tenantId);

        var caller = User(managerUserId, PermissionCatalog.Performance.SetGoalTeam);
        var result = await Service(caller).GetByIdAsync(goalId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("not_direct_report");
    }

    [Fact]
    [Trait("TC", "TC-PRF-001-14")]
    public async Task GetById_TeamManager_ReadingOwnReportsGoal_Allowed200()
    {
        SeedTenant();
        var (managerUserId, managerEmpId) = SeedManager();
        // The goal owner is a DIRECT report of this manager.
        SeedOwnerEmployee(reportsTo: managerEmpId);
        var goalId = SeedGoal(_tenantId);

        var caller = User(managerUserId, PermissionCatalog.Performance.SetGoalTeam);
        var result = await Service(caller).GetByIdAsync(goalId);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Id.Should().Be(goalId);
    }

    [Fact]
    [Trait("TC", "TC-PRF-001-14")]
    public async Task GetById_Hr_SetGoalAll_Allowed200()
    {
        SeedTenant();
        // Owner reports to nobody in particular; HR override still allows.
        SeedOwnerEmployee(reportsTo: Guid.NewGuid());
        var goalId = SeedGoal(_tenantId);

        var result = await Service(HrUser()).GetByIdAsync(goalId);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Id.Should().Be(goalId);
    }

    [Fact]
    [Trait("TC", "TC-PRF-001-14")]
    public async Task GetById_GoalOwner_ReadingOwnGoal_Allowed200_SelfBranch()
    {
        // The goal's OWNER (Employee.UserId == caller UserId) has NO SetGoal permissions at all, yet must
        // still be able to read their own goal — the self-read branch, so DF-18 authz doesn't regress the
        // owner's access to their own goal.
        SeedTenant();
        SeedOwnerEmployee();
        var goalId = SeedGoal(_tenantId);

        var caller = User(_ownerUserId); // no permissions
        var result = await Service(caller).GetByIdAsync(goalId);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Id.Should().Be(goalId);
        result.Value.EmployeeId.Should().Be(_employeeId);
    }
}
