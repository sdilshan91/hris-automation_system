// ============================================================================
// US-ADM-003 / ISSUE-001: GET /system/impersonation/targets must return 400
// (missing_required_parameter) when the required tenantId is ABSENT (binds to
// Guid.Empty) rather than 404 tenant_not_found. 404 is reserved for a supplied-
// but-unknown id (produced by the service/handler). A valid id dispatches.
// ============================================================================

using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Application.DTOs;
using HRM.Application.Common.Models;
using HRM.Application.Features.Impersonation.DTOs;
using HRM.Application.Features.Impersonation.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AdminImpersonationTargetsControllerTests
{
    private static AdminImpersonationController Build(IMediator mediator) =>
        new(mediator)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task ListTargets_MissingTenantId_Returns400_AndDoesNotDispatch()
    {
        var mediator = Substitute.For<IMediator>();
        var controller = Build(mediator);

        var result = await controller.ListTargets(Guid.Empty, default);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Which;
        bad.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        bad.Value.Should().BeOfType<ApiResponse>().Which.Code.Should().Be("missing_required_parameter");

        await mediator.DidNotReceive()
            .Send(Arg.Any<ListImpersonationTargetsQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListTargets_ValidTenantId_Dispatches()
    {
        var tenantId = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<ListImpersonationTargetsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ImpersonationTargetDto>>.Success(Array.Empty<ImpersonationTargetDto>()));

        var controller = Build(mediator);

        var result = await controller.ListTargets(tenantId, default);

        result.Should().BeOfType<OkObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status200OK);
        await mediator.Received(1).Send(
            Arg.Is<ListImpersonationTargetsQuery>(q => q.TargetTenantId == tenantId),
            Arg.Any<CancellationToken>());
    }
}
