// ============================================================================
// ISSUE-047 regression: the report/analytics/export route segments must accept
// the kebab-case form the US-LV-012 test cases and the FE use (balance-summary,
// lop-summary, utilization-by-department), not only the PascalCase enum name.
// Enum.TryParse is case- but NOT separator-insensitive, so the controller strips
// '-'/'_' before parsing. A truly unknown value still 400s and does NOT dispatch.
// ============================================================================

using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;
using HRM.Application.Features.LeaveReports.Queries;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveReportsControllerKebabCaseTests
{
    private static LeaveReportsController Build(IMediator mediator) =>
        new(mediator)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    [Fact]
    public async Task GetReport_KebabCaseType_Resolves_AndDispatches_ISSUE047()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetLeaveReportQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<LeaveReportResult>.Success(new LeaveReportResult()));

        var controller = Build(mediator);

        var result = await controller.GetReport("balance-summary", new LeaveReportQueryParams(), default);

        // Parsed successfully → 200 OK, and the query was dispatched with the correct enum value.
        result.Should().BeOfType<OkObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status200OK);
        await mediator.Received(1).Send(
            Arg.Is<GetLeaveReportQuery>(q => q.ReportType == LeaveReportType.BalanceSummary),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetReport_UnknownType_Returns400_AndDoesNotDispatch_ISSUE047()
    {
        var mediator = Substitute.For<IMediator>();
        var controller = Build(mediator);

        var result = await controller.GetReport("not-a-real-report", new LeaveReportQueryParams(), default);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        await mediator.DidNotReceive().Send(Arg.Any<GetLeaveReportQuery>(), Arg.Any<CancellationToken>());
    }
}
