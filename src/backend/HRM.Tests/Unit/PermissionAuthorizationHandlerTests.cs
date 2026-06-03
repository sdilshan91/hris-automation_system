// ============================================================================
// US-AUTH-006: RBAC Unit Tests - PermissionAuthorizationHandler
// Tests the policy-based authorization handler (FR-5 Layer 2).
// AC-4: 403 when missing permission.
// ============================================================================

using System.Security.Claims;
using FluentAssertions;
using HRM.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class PermissionAuthorizationHandlerTests
{
    private readonly PermissionAuthorizationHandler _handler;

    public PermissionAuthorizationHandlerTests()
    {
        var logger = Substitute.For<ILogger<PermissionAuthorizationHandler>>();
        _handler = new PermissionAuthorizationHandler(logger);
    }

    [Fact]
    public async Task Handle_UserHasRequiredPermission_ShouldSucceed()
    {
        // Arrange
        var requirement = new PermissionRequirement("Payroll.View");
        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim("permissions", "Payroll.View"),
            new Claim("permissions", "Leave.Apply"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            resource: null);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_UserLacksRequiredPermission_ShouldNotSucceed()
    {
        // Arrange: AC-4 scenario - Employee lacks Payroll.View
        var requirement = new PermissionRequirement("Payroll.View");
        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
            new Claim("permissions", "Leave.Apply"),
            new Claim("permissions", "Employee.View.Own"),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            resource: null);

        // Act
        await _handler.HandleAsync(context);

        // Assert: handler does not call Succeed, so HasSucceeded remains false
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UserHasNoPermissionClaims_ShouldNotSucceed()
    {
        var requirement = new PermissionRequirement("Payroll.View");
        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("tenant_id", Guid.NewGuid().ToString()),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            resource: null);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UnauthenticatedUser_ShouldNotSucceed()
    {
        var requirement = new PermissionRequirement("Payroll.View");
        var principal = new ClaimsPrincipal(new ClaimsIdentity()); // not authenticated

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            resource: null);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Theory]
    [InlineData("Leave.Apply")]
    [InlineData("Roles.Manage")]
    [InlineData("Employee.View.All")]
    public async Task Handle_VariousPermissions_UserHasIt_ShouldSucceed(string permission)
    {
        var requirement = new PermissionRequirement(permission);
        var claims = new[]
        {
            new Claim("sub", Guid.NewGuid().ToString()),
            new Claim("permissions", permission),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            principal,
            resource: null);

        await _handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }
}
