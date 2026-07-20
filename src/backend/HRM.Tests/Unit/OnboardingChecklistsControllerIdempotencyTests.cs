// ============================================================================
// US-ONB-002 — OnboardingChecklistsController: Idempotency-Key header-vs-body precedence.
//
// DF-10: the assign action resolves the idempotency key as "header, else body"
// (`string.IsNullOrWhiteSpace(headerKey) ? request.IdempotencyKey : headerKey`, NFR-5).
// A mutation swapping that precedence had no test. These arms capture the dispatched
// AssignChecklistCommand and assert which key wins. Send is stubbed to a FAILURE result so the
// action returns via StatusCode (no CreatedAtAction / HttpContext needed).
// ============================================================================

using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Application.Common.Models;
using HRM.Application.Features.Onboarding.Commands;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Enums;
using MediatR;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class OnboardingChecklistsControllerIdempotencyTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private OnboardingChecklistsController Controller()
    {
        // Send returns a failure so Assign short-circuits via StatusCode (no CreatedAtAction → no HttpContext).
        _mediator.Send(Arg.Any<AssignChecklistCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<OnboardingChecklistInstanceDto>.Failure("stop after capture", 409));
        return new OnboardingChecklistsController(_mediator);
    }

    private static AssignChecklistRequest Request(string? bodyKey) => new()
    {
        EmployeeId = Guid.NewGuid(),
        TemplateId = Guid.NewGuid(),
        Mode = ChecklistAssignmentMode.Replace,
        IdempotencyKey = bodyKey,
    };

    // ── DF-10: the header key wins when BOTH header and body are present. ──
    [Fact]
    [Trait("TC", "TC-ONB-002-14")]
    public async Task Assign_HeaderKeyPresent_WinsOverBodyKey()
    {
        await Controller().Assign(Request(bodyKey: "BODY-KEY"), idempotencyKey: "HEADER-KEY", CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<AssignChecklistCommand>(c => c.IdempotencyKey == "HEADER-KEY"), Arg.Any<CancellationToken>());
    }

    // ── DF-10: a blank/whitespace header falls back to the body key. ──
    [Theory]
    [Trait("TC", "TC-ONB-002-14")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Assign_HeaderKeyBlank_FallsBackToBodyKey(string? headerKey)
    {
        await Controller().Assign(Request(bodyKey: "BODY-KEY"), idempotencyKey: headerKey, CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<AssignChecklistCommand>(c => c.IdempotencyKey == "BODY-KEY"), Arg.Any<CancellationToken>());
    }

    // ── DF-10: header present, body absent → header key still flows through. ──
    [Fact]
    [Trait("TC", "TC-ONB-002-14")]
    public async Task Assign_OnlyHeaderKey_FlowsThrough()
    {
        await Controller().Assign(Request(bodyKey: null), idempotencyKey: "HEADER-KEY", CancellationToken.None);

        await _mediator.Received(1).Send(
            Arg.Is<AssignChecklistCommand>(c => c.IdempotencyKey == "HEADER-KEY"), Arg.Any<CancellationToken>());
    }
}
