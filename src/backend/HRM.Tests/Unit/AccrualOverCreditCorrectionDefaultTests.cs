// ============================================================================
// BUG-291 correction — the dry-run default.
//
// This covers one line, and it is the most dangerous line in the change. The endpoint originally bound
// `[FromQuery] bool dryRun`, which ASP.NET binds to FALSE when the parameter is omitted — so
//
//     POST /api/v1/tenant/leave-entitlements/accrual-over-credit-correction?asOfDate=2026-03-31
//
// would have silently APPLIED a deduction to every affected employee's visible leave balance. The parameter
// is now `bool?`, and null means dry run.
//
// No service-level test can catch that: the binding happens above the service, and every other arm passes
// dryRun explicitly. This asserts the DEFAULT, which is the only case a careless caller will ever hit.
// ============================================================================

using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveEntitlements.Commands;
using HRM.Application.Features.LeaveEntitlements.DTOs;
using MediatR;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AccrualOverCreditCorrectionDefaultTests
{
    private static (LeaveEntitlementsController Controller, IMediator Mediator) Build()
    {
        var mediator = Substitute.For<IMediator>();
        mediator
            .Send(Arg.Any<CorrectAccrualOverCreditCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<AccrualOverCreditCorrectionResultDto>.Success(
                new AccrualOverCreditCorrectionResultDto()));

        return (new LeaveEntitlementsController(mediator), mediator);
    }

    [Fact]
    public async Task Omitting_dryRun_means_DRY_RUN_not_apply_BUG291()
    {
        var (controller, mediator) = Build();

        await controller.CorrectAccrualOverCredit(new DateOnly(2026, 3, 31), dryRun: null, CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<CorrectAccrualOverCreditCommand>(c => c.DryRun == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Applying_requires_dryRun_to_be_EXPLICITLY_false_BUG291()
    {
        var (controller, mediator) = Build();

        await controller.CorrectAccrualOverCredit(new DateOnly(2026, 3, 31), dryRun: false, CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<CorrectAccrualOverCreditCommand>(c => c.DryRun == false),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task An_explicit_true_is_still_a_dry_run_BUG291()
    {
        var (controller, mediator) = Build();

        await controller.CorrectAccrualOverCredit(new DateOnly(2026, 3, 31), dryRun: true, CancellationToken.None);

        await mediator.Received(1).Send(
            Arg.Is<CorrectAccrualOverCreditCommand>(c => c.DryRun == true),
            Arg.Any<CancellationToken>());
    }
}
