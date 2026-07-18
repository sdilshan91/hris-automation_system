// ============================================================================
// ISSUE-099 (US-PRF-001) — GET /api/v1/tenant/performance/goals/{id} was a stub that
// returned 200 + empty body for ANY id (non-existent OR foreign-tenant). GoalService now
// has a real GetByIdAsync: tenant-scoped via the EF global query filter, returning the goal
// on 200 and a 404 (goal_not_found) for an unknown or foreign-tenant id (invisible through
// the filter -> never leaks).
//
// Drives the REAL GoalService through the InMemory-through-real-EF harness (goals are plain
// rows; the global query filter is exercised). Mirrors GoalServiceSaveGoalsTests.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
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
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPerformanceNotificationService _notifications;

    public GoalServiceGetByIdTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Email.Returns("hr@acme.com");

        _notifications = Substitute.For<IPerformanceNotificationService>();
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private GoalService Service() => new(
        Db(), _tenantContext, _currentUser, _notifications, Substitute.For<ILogger<GoalService>>());

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
    public async Task GetById_ExistingInTenantGoal_Returns200WithDto()
    {
        var goalId = SeedGoal(_tenantId);

        var result = await Service().GetByIdAsync(goalId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be(goalId);
        result.Value.Title.Should().Be("Ship it");
        result.Value.Weight.Should().Be(40);
    }

    [Fact]
    public async Task GetById_UnknownId_Returns404GoalNotFound()
    {
        SeedGoal(_tenantId); // some other goal exists

        var result = await Service().GetByIdAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("goal_not_found");
    }

    [Fact]
    public async Task GetById_ForeignTenantGoal_IsInvisible_Returns404()
    {
        // A goal owned by ANOTHER tenant is filtered out by the global query filter (keyed off the
        // resolved tenant), so it is indistinguishable from a non-existent id -> 404, never leaked.
        var foreignGoalId = SeedGoal(Guid.NewGuid());

        var result = await Service().GetByIdAsync(foreignGoalId);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("goal_not_found");
    }
}
