// ============================================================================
// ISSUE-088 regression (US-ATT-009 — attendance period lock body validation).
//
// AttendancePayrollService.LockPeriodAsync must REJECT an empty / default-date lock
// body (both dates default(DateOnly) == 0001-01-01, i.e. an omitted request body)
// with 400 (a real date range still succeeds). Pre-fix the only date guard was
// `periodEnd < periodStart`; two equal default dates slipped past it and a lock row
// for the 0001-01-01 range was created, so this reject test FAILS pre-fix (the call
// returns IsSuccess and a row is persisted) and PASSES once the parallel fix adds the
// empty/default-date guard.
//
// InMemory is appropriate: this is app-level input validation on a single write.
// Traceability: @ISSUE-088.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class AttendancePeriodLockValidationTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public AttendancePeriodLockValidationTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsAuthenticated.Returns(true);
    }

    // ── Reject: empty / default-date lock body ───────────────────────────────

    [Fact]
    public async Task LockPeriod_EmptyDefaultDates_IsRejected_ISSUE088()
    {
        var service = CreateService();

        // default(DateOnly) for both bounds — what an omitted request body deserializes to.
        var result = await service.LockPeriodAsync(default, default);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);

        // And no bogus 0001-01-01 lock row was persisted.
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        db.AttendancePeriodLocks.Count().Should().Be(0);
    }

    // ── Control: a real date range still locks successfully ──────────────────

    [Fact]
    public async Task LockPeriod_ValidDateRange_Succeeds_ISSUE088()
    {
        var service = CreateService();

        var start = new DateOnly(2026, 6, 1);
        var end = new DateOnly(2026, 6, 30);
        var result = await service.LockPeriodAsync(start, end);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.PeriodStart.Should().Be(start);
        result.Value.PeriodEnd.Should().Be(end);
        result.Value.IsLocked.Should().BeTrue();

        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        db.AttendancePeriodLocks.Count().Should().Be(1);
    }

    private AttendancePayrollService CreateService()
        => new(
            TestDbContextFactory.Create(_tenantContext, _dbName),
            _tenantContext,
            _currentUser,
            Substitute.For<IAttendanceSummaryService>(),
            NullLogger<AttendancePayrollService>.Instance);
}
