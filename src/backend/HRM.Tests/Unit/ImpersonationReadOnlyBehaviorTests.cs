// ============================================================================
// US-ADM-003: ImpersonationReadOnlyBehavior unit tests (AC-5/AC-6/FR-3/FR-6/BR-1).
// The security gate that constrains what a request may do during an impersonation
// session. Two independent gates, both 403 (ForbiddenException):
//   - read-only session (System Support / Suspended tenant) blocks every *Command
//   - destructive ops (password/role/delete) blocked even for a FULL admin session
// Non-impersonated traffic and *Query reads pass through.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Behaviors;
using HRM.Application.Common.Exceptions;
using HRM.Application.Common.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ImpersonationReadOnlyBehaviorTests
{
    // Minimal request types whose NAMES drive the behavior's structural checks.
    private sealed record OrdinaryCommand : IRequest<string>;
    private sealed record OrdinaryQuery : IRequest<string>;
    private sealed record DeleteUserCommand : IRequest<string>;
    private sealed record ChangePasswordCommand : IRequest<string>;
    private sealed record EndImpersonationCommand : IRequest<string>;

    private static ICurrentUser User(bool impersonating, bool readOnly)
    {
        var u = Substitute.For<ICurrentUser>();
        u.IsImpersonating.Returns(impersonating);
        u.ImpersonationReadOnly.Returns(readOnly);
        u.ImpersonationSessionId.Returns(Guid.NewGuid());
        u.ImpersonatorId.Returns(Guid.NewGuid());
        return u;
    }

    private static ImpersonationReadOnlyBehavior<TReq, string> Behavior<TReq>(ICurrentUser user)
        where TReq : notnull
        => new(user, NullLogger<ImpersonationReadOnlyBehavior<TReq, string>>.Instance);

    private static RequestHandlerDelegate<string> Next(string value = "ok") => _ => Task.FromResult(value);

    [Fact]
    public async Task NotImpersonating_WriteCommand_PassesThrough()
    {
        var behavior = Behavior<OrdinaryCommand>(User(impersonating: false, readOnly: false));
        var result = await behavior.Handle(new OrdinaryCommand(), Next(), default);
        result.Should().Be("ok");
    }

    [Fact]
    public async Task ReadOnlySession_WriteCommand_IsForbidden()
    {
        var behavior = Behavior<OrdinaryCommand>(User(impersonating: true, readOnly: true));
        var act = () => behavior.Handle(new OrdinaryCommand(), Next(), default);
        (await act.Should().ThrowAsync<ForbiddenException>()).WithMessage("*read-only*");
    }

    [Fact]
    public async Task ReadOnlySession_Query_PassesThrough()
    {
        var behavior = Behavior<OrdinaryQuery>(User(impersonating: true, readOnly: true));
        var result = await behavior.Handle(new OrdinaryQuery(), Next(), default);
        result.Should().Be("ok");
    }

    [Fact]
    public async Task FullImpersonation_DestructiveCommand_IsForbidden()
    {
        // FR-6: a delete is blocked even when the session is NOT read-only (full System Admin impersonation).
        var behavior = Behavior<DeleteUserCommand>(User(impersonating: true, readOnly: false));
        var act = () => behavior.Handle(new DeleteUserCommand(), Next(), default);
        (await act.Should().ThrowAsync<ForbiddenException>()).WithMessage("*not permitted*");
    }

    [Fact]
    public async Task FullImpersonation_ChangePassword_IsForbidden()
    {
        var behavior = Behavior<ChangePasswordCommand>(User(impersonating: true, readOnly: false));
        var act = () => behavior.Handle(new ChangePasswordCommand(), Next(), default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task ReadOnlySession_EndImpersonation_IsAlwaysAllowed()
    {
        // AC-3: the operator must be able to end the session even under a read-only impersonation.
        var behavior = Behavior<EndImpersonationCommand>(User(impersonating: true, readOnly: true));
        var result = await behavior.Handle(new EndImpersonationCommand(), Next(), default);
        result.Should().Be("ok");
    }

    [Fact]
    public async Task FullImpersonation_OrdinaryWrite_PassesThrough()
    {
        // A non-destructive write under a NON-read-only session is allowed (e.g. reproducing a normal edit).
        var behavior = Behavior<OrdinaryCommand>(User(impersonating: true, readOnly: false));
        var result = await behavior.Handle(new OrdinaryCommand(), Next(), default);
        result.Should().Be("ok");
    }
}
