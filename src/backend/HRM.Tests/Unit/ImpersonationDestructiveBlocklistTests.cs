// ============================================================================
// BUG-107 regression: the impersonation FR-6 destructive-op blocklist must catch
// ForcePasswordReset / DeactivateUser / AssignUserRoles / EditUserRoles — the
// prior markers missed them (no substring overlap), so a support operator could
// run them while impersonating.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Behaviors;
using HRM.Application.Common.Exceptions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Roles.Commands;
using HRM.Application.Features.Users.Commands;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ImpersonationDestructiveBlocklistTests
{
    private static ICurrentUser Impersonating()
    {
        var u = Substitute.For<ICurrentUser>();
        u.IsImpersonating.Returns(true);
        u.ImpersonationReadOnly.Returns(false); // full (non-read-only) impersonation — destructive still blocked
        u.ImpersonationSessionId.Returns(Guid.NewGuid());
        u.ImpersonatorId.Returns(Guid.NewGuid());
        return u;
    }

    private static async Task AssertBlockedAsync<TCommand>(TCommand command)
    {
        var behavior = new ImpersonationReadOnlyBehavior<TCommand, Result>(
            Impersonating(), NullLogger<ImpersonationReadOnlyBehavior<TCommand, Result>>.Instance);

        var nextCalled = false;
        RequestHandlerDelegate<Result> next = _ => { nextCalled = true; return Task.FromResult(Result.Success()); };

        var act = async () => await behavior.Handle(command, next, default);

        await act.Should().ThrowAsync<ForbiddenException>();
        nextCalled.Should().BeFalse("the destructive command must be short-circuited");
    }

    [Fact]
    public Task ForcePasswordReset_IsBlocked()
        => AssertBlockedAsync(new ForcePasswordResetCommand(Guid.NewGuid()));

    [Fact]
    public Task DeactivateUser_IsBlocked()
        => AssertBlockedAsync(new DeactivateUserCommand(Guid.NewGuid()));

    [Fact]
    public Task AssignUserRoles_IsBlocked()
        => AssertBlockedAsync(new AssignUserRolesCommand(Guid.NewGuid(), new[] { Guid.NewGuid() }));

    [Fact]
    public Task EditUserRoles_IsBlocked()
        => AssertBlockedAsync(new EditUserRolesCommand(Guid.NewGuid(), new[] { Guid.NewGuid() }));
}
