// ============================================================================
// ISSUE-092 (US-ATT-009 FR / TC-ATT-127 S5) — GET /api/v1/attendance/payroll-data
// must reject a malformed employeeIds filter with a 400 (machine-readable
// invalid_employee_ids) instead of silently dropping the bad tokens and widening
// scope to the WHOLE tenant. An absent/blank filter still means "all employees".
//
// Drives AttendanceController directly with a mocked IMediator: the parser 400s before
// the query is ever dispatched, so the mediator must NOT be called for bad input, and
// MUST be called (with the parsed / null ids) for valid input.
// ============================================================================

using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Application.DTOs;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Application.Features.Attendance.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AttendancePayrollEmployeeIdsValidationTests
{
    private static AttendanceController Controller(IMediator mediator) => new(mediator);

    [Theory]
    [InlineData("notauuid,123")]  // all-invalid tokens
    [InlineData("';DROP--")]       // garbage
    [InlineData(",, ,")]           // only separators -> no ids
    public async Task GetPayrollData_MalformedEmployeeIds_Returns400_DoesNotDispatch(string employeeIds)
    {
        var mediator = Substitute.For<IMediator>();
        var controller = Controller(mediator);

        var result = await controller.GetPayrollData("2026-06", employeeIds, CancellationToken.None);

        var obj = result.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(400);
        obj.Value.Should().BeOfType<ApiResponse>()
            .Which.Code.Should().Be("invalid_employee_ids");

        // Crux of ISSUE-092: a bad filter must NOT silently fall through to the whole-tenant query.
        await mediator.DidNotReceive().Send(Arg.Any<GetPayrollDataQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPayrollData_NoEmployeeIds_DispatchesWithNullFilter_AllEmployees()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetPayrollDataQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<AttendancePayrollResult>.Success(new AttendancePayrollResult { Period = "2026-06" }));
        var controller = Controller(mediator);

        var result = await controller.GetPayrollData("2026-06", null, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await mediator.Received(1).Send(
            Arg.Is<GetPayrollDataQuery>(q => q.EmployeeIds == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetPayrollData_ValidEmployeeIds_DispatchesWithParsedIds()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetPayrollDataQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<AttendancePayrollResult>.Success(new AttendancePayrollResult { Period = "2026-06" }));
        var controller = Controller(mediator);

        var result = await controller.GetPayrollData("2026-06", $"{id1},{id2}", CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        await mediator.Received(1).Send(
            Arg.Is<GetPayrollDataQuery>(q => q.EmployeeIds != null
                && q.EmployeeIds.Count == 2
                && q.EmployeeIds.Contains(id1)
                && q.EmployeeIds.Contains(id2)),
            Arg.Any<CancellationToken>());
    }
}
