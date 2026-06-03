// ============================================================================
// US-AUTH-006: RBAC Unit Tests - TeamScopeAuthorizationHandler
// Tests resource-based authorization (FR-5 Layer 3).
// AC-5: Manager can only act on direct reports.
// ============================================================================

using System.Security.Claims;
using FluentAssertions;
using HRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class TeamScopeAuthorizationHandlerTests
{
    private readonly TeamScopeAuthorizationHandler _handler;

    public TeamScopeAuthorizationHandlerTests()
    {
        var logger = Substitute.For<ILogger<TeamScopeAuthorizationHandler>>();
        _handler = new TeamScopeAuthorizationHandler(logger);
    }

    [Fact]
    public async Task Handle_ManagerIsDirectReport_ShouldSucceed()
    {
        // Arrange
        var managerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var requirement = new TeamScopeRequirement("Leave.Approve.Team");
        var resource = new TeamScopeResource
        {
            TargetEmployeeUserId = employeeId,
            ManagerUserId = managerId,
        };

        var claims = new[]
        {
            new Claim("sub", managerId.ToString()),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim("permissions", "Leave.Approve.Team"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            resource);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ManagerIsNotDirectReport_ShouldDeny()
    {
        // Arrange: AC-5 - manager tries to approve for non-report
        var managerId = Guid.NewGuid();
        var otherManagerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var requirement = new TeamScopeRequirement("Leave.Approve.Team");
        var resource = new TeamScopeResource
        {
            TargetEmployeeUserId = employeeId,
            ManagerUserId = otherManagerId, // different manager
        };

        var claims = new[]
        {
            new Claim("sub", managerId.ToString()),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim("permissions", "Leave.Approve.Team"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            resource);

        // Act
        await _handler.HandleAsync(context);

        // Assert: Should NOT succeed (403 scenario)
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UserWithAllScope_ShouldBypassTeamCheck()
    {
        // Arrange: HR admin with Leave.Approve.All should bypass team check
        var hrAdminId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var someOtherManagerId = Guid.NewGuid();

        var requirement = new TeamScopeRequirement("Leave.Approve.Team");
        var resource = new TeamScopeResource
        {
            TargetEmployeeUserId = employeeId,
            ManagerUserId = someOtherManagerId,
        };

        var claims = new[]
        {
            new Claim("sub", hrAdminId.ToString()),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim("permissions", "Leave.Approve.Team"),
            new Claim("permissions", "Leave.Approve.All"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            resource);

        // Act
        await _handler.HandleAsync(context);

        // Assert: Should succeed because user has .All scope
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UserLacksPermissionClaim_ShouldDeny()
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        var requirement = new TeamScopeRequirement("Leave.Approve.Team");
        var resource = new TeamScopeResource
        {
            TargetEmployeeUserId = employeeId,
            ManagerUserId = userId,
        };

        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("permissions", "Leave.Apply"), // wrong permission
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            resource);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NullManagerUserId_ShouldDeny()
    {
        var userId = Guid.NewGuid();

        var requirement = new TeamScopeRequirement("Leave.Approve.Team");
        var resource = new TeamScopeResource
        {
            TargetEmployeeUserId = Guid.NewGuid(),
            ManagerUserId = null, // employee has no manager set
        };

        var claims = new[]
        {
            new Claim("sub", userId.ToString()),
            new Claim("permissions", "Leave.Approve.Team"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            resource);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
