// ============================================================================
// BUG-042 regression: a tenant switch during an impersonation session must be
// rejected (403) — otherwise the switch mints a fresh, non-impersonation token
// and escapes the audited/time-boxed session (BR-4).
// ============================================================================

using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Auth.Commands;
using HRM.Application.Features.Auth.DTOs;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AuthControllerSwitchTenantTests
{
    private static AuthController Build(IMediator mediator, ICurrentUser currentUser)
    {
        return new AuthController(mediator, currentUser)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    [Fact]
    public async Task SwitchTenant_WhileImpersonating_Returns403_AndDoesNotDispatch()
    {
        var mediator = Substitute.For<IMediator>();
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsImpersonating.Returns(true);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.TenantId.Returns(Guid.NewGuid());

        var controller = Build(mediator, currentUser);

        var result = await controller.SwitchTenant(new SwitchTenantRequest(Guid.NewGuid()), default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        await mediator.DidNotReceive().Send(Arg.Any<SwitchTenantCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SwitchTenant_WhenNotImpersonating_Dispatches()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<SwitchTenantCommand>(), Arg.Any<CancellationToken>())
            .Returns(HRM.Application.Common.Models.Result<SwitchTenantResponse>.Failure("stop here", 400));

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.IsImpersonating.Returns(false);
        currentUser.UserId.Returns(Guid.NewGuid());
        currentUser.TenantId.Returns(Guid.NewGuid());

        var controller = Build(mediator, currentUser);

        await controller.SwitchTenant(new SwitchTenantRequest(Guid.NewGuid()), default);

        // The guard did NOT short-circuit — the command was dispatched.
        await mediator.Received(1).Send(Arg.Any<SwitchTenantCommand>(), Arg.Any<CancellationToken>());
    }
}
