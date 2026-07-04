// ============================================================================
// BUG-033 (Leave Management, US-LV-008 FR-5/AC-5) regression.
//
// GetCarryForwardPreviewQueryHandler previously passed request.Year straight to
// ILeaveCarryForwardService.PreviewYearEndAsync with no range guard. An out-of-range year
// (e.g. 99999) flows down into the entitlement/date math (`new DateOnly(year, ...)` / the
// leave-year 2000..2100 validators) and throws ArgumentOutOfRangeException → unhandled → HTTP 500.
//
// The fix adds a range guard in the handler: a Year outside 2000..2100 returns a clean
// Result.Failure(400) and NEVER calls the service — mirroring ComputeEffectiveEntitlementValidator
// / UpsertLeaveEntitlementOverride. A valid year (or null → current year) still previews.
//
// WHY INMEMORY/UNIT (not Postgres): this is pure input validation in the handler, not a DB race —
// no schema or Postgres-specific behaviour is involved. The service is mocked so the assertions are
// about the HANDLER'S guard: (a) out-of-range → 400 and the service is never reached (so the
// downstream 500 can never happen), and (b) valid years still reach the service.
//
// The mock is configured to THROW ArgumentOutOfRangeException for out-of-range years, faithfully
// standing in for the real downstream 500. That makes the "Not500" claim concrete:
//   Pre-fix : the handler calls the service with the raw year → the mock throws → Handle throws (500).
//   Post-fix: the guard short-circuits to 400 → the service is never called → no throw.
// The independent DidNotReceive()/Received() assertions also fail pre-fix on their own (pre-fix the
// service IS called and its result — not a 400 — is returned), so the guard is verified two ways.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveCarryForward.DTOs;
using HRM.Application.Features.LeaveCarryForward.Queries;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace HRM.Tests.Unit;

public sealed class GetCarryForwardPreviewQueryHandlerTests
{
    private readonly ILeaveCarryForwardService _service = Substitute.For<ILeaveCarryForwardService>();

    private GetCarryForwardPreviewQueryHandler CreateHandler()
    {
        // Faithful stand-in for the real downstream: out-of-range years crash (the BUG-033 500);
        // in-range years produce a preview.
        _service.PreviewYearEndAsync(Arg.Is<int>(y => y < 2000 || y > 2100), Arg.Any<CancellationToken>())
            .ThrowsAsync(ci => new ArgumentOutOfRangeException(nameof(GetCarryForwardPreviewQuery.Year)));
        _service.PreviewYearEndAsync(Arg.Is<int>(y => y >= 2000 && y <= 2100), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<CarryForwardPreviewDto>>.Success(Array.Empty<CarryForwardPreviewDto>()));

        return new GetCarryForwardPreviewQueryHandler(_service);
    }

    // ── BUG-033: out-of-range year → clean 400, never a 500 ──
    [Theory]
    [InlineData(1999)]   // just below the lower bound
    [InlineData(2101)]   // just above the upper bound
    [InlineData(1800)]   // far-below business bound
    [InlineData(99999)]  // year > 9999: the real DateOnly/DateTime that throws → the actual 500
    public async Task CarryForwardPreview_OutOfRangeYear_Returns400Not500_BUG033(int badYear)
    {
        var handler = CreateHandler();

        var act = async () => await handler.Handle(new GetCarryForwardPreviewQuery(badYear), CancellationToken.None);

        // Post-fix: the guard returns 400 without ever touching the (unsafe) downstream. Pre-fix: the
        // handler forwards the raw year and the service throws ArgumentOutOfRangeException — a 500.
        var result = (await act.Should().NotThrowAsync(
                "an out-of-range leave year must be a clean 400, not an unhandled ArgumentOutOfRangeException (500)"))
            .Subject;

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);

        // The guard must short-circuit BEFORE the service — the only place the 500 could originate.
        await _service.DidNotReceive().PreviewYearEndAsync(badYear, Arg.Any<CancellationToken>());
    }

    // ── Positive control: a valid year still returns a preview (guard does not over-block) ──
    [Fact]
    public async Task CarryForwardPreview_ValidYear_ReturnsPreview_BUG033()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCarryForwardPreviewQuery(2025), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        await _service.Received(1).PreviewYearEndAsync(2025, Arg.Any<CancellationToken>());
    }

    // ── Positive control: null year defaults to the current year (in range) and still previews ──
    [Fact]
    public async Task CarryForwardPreview_NullYear_UsesCurrentYear_BUG033()
    {
        var handler = CreateHandler();

        var result = await handler.Handle(new GetCarryForwardPreviewQuery(null), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.Error);
        await _service.Received(1).PreviewYearEndAsync(DateTime.UtcNow.Year, Arg.Any<CancellationToken>());
    }
}
