// ============================================================================
// US-RPT-005: Dashboard with KPI Widgets — pure domain-rule unit tests.
// Covers the load-bearing pure logic exercised by DashboardService:
//   - BR-1: server-side role resolution (hr > manager > employee precedence).
//   - BR-4: "upcoming birthdays/anniversaries within the next 7 days" window
//           (recurring month/day projection, inclusive boundaries, year wrap).
// The composed widget VALUES + tenant isolation + BR-3 are verified end-to-end
// in DashboardIntegrationTests; here we lock the primitives those depend on.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Authorization;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class DashboardServiceTests
{
    // ── BR-1: role resolution precedence ────────────────────────────────────

    [Fact]
    public void ResolveRole_HrViewAll_IsHr_EvenWithDirectReports()
    {
        // HR precedence wins over the manager check (an HR Officer who also has reports is still "hr").
        var role = DashboardService.ResolveRole(
            [PermissionCatalog.Employee.ViewAll], [PermissionCatalog.BuiltInRoles.HROfficer], hasDirectReports: true);
        role.Should().Be("hr");
    }

    [Theory]
    [InlineData("Leave.View.All")]
    [InlineData("Attendance.View.All")]
    [InlineData("Employee.View.All")]
    public void ResolveRole_AnyCrossOrgViewAll_IsHr(string perm)
        => DashboardService.ResolveRole([perm], [], hasDirectReports: false).Should().Be("hr");

    [Fact]
    public void ResolveRole_DirectReports_NoHrPerm_IsManager()
        => DashboardService.ResolveRole([PermissionCatalog.Employee.ViewTeam], [], hasDirectReports: true)
            .Should().Be("manager");

    [Fact]
    public void ResolveRole_ManagerRole_NoDirectReports_IsManager()
        => DashboardService.ResolveRole([], [PermissionCatalog.BuiltInRoles.Manager], hasDirectReports: false)
            .Should().Be("manager");

    [Fact]
    public void ResolveRole_PlainEmployee_IsEmployee()
        => DashboardService.ResolveRole(
            [PermissionCatalog.Employee.ViewOwn, PermissionCatalog.Leave.Apply],
            [PermissionCatalog.BuiltInRoles.Employee], hasDirectReports: false)
            .Should().Be("employee");

    [Fact]
    public void ResolveRole_NullInputs_DefaultsToEmployee()
        => DashboardService.ResolveRole(null!, null!, hasDirectReports: false).Should().Be("employee");

    // ── BR-4: upcoming birthday/anniversary window (next 7 days) ─────────────

    [Fact]
    public void IsWithinNextDays_Today_IsIncluded()
    {
        var today = new DateOnly(2026, 6, 17);
        var dob = new DateOnly(1990, 6, 17); // birthday today
        DashboardService.IsWithinNextDays(dob, today, 7, out var occ).Should().BeTrue();
        occ.Should().Be(new DateOnly(2026, 6, 17));
    }

    [Fact]
    public void IsWithinNextDays_SeventhDay_IsIncluded_BoundaryInclusive()
    {
        var today = new DateOnly(2026, 6, 17);
        var dob = new DateOnly(1985, 6, 24); // exactly 7 days away
        DashboardService.IsWithinNextDays(dob, today, 7, out _).Should().BeTrue();
    }

    [Fact]
    public void IsWithinNextDays_EighthDay_IsExcluded()
    {
        var today = new DateOnly(2026, 6, 17);
        var dob = new DateOnly(1985, 6, 25); // 8 days away — outside the window
        DashboardService.IsWithinNextDays(dob, today, 7, out _).Should().BeFalse();
    }

    [Fact]
    public void IsWithinNextDays_AlreadyPassedThisYear_IsExcluded()
    {
        var today = new DateOnly(2026, 6, 17);
        var dob = new DateOnly(1990, 6, 10); // a week ago — projects to NEXT year, far outside 7 days
        DashboardService.IsWithinNextDays(dob, today, 7, out _).Should().BeFalse();
    }

    [Fact]
    public void IsWithinNextDays_WrapsYearBoundary()
    {
        var today = new DateOnly(2026, 12, 30);
        var dob = new DateOnly(1990, 1, 2); // Jan 2 next year is 3 days from Dec 30
        DashboardService.IsWithinNextDays(dob, today, 7, out var occ).Should().BeTrue();
        occ.Year.Should().Be(2027);
        occ.Month.Should().Be(1);
        occ.Day.Should().Be(2);
    }

    [Fact]
    public void IsWithinNextDays_Feb29_FallsBackToFeb28_InNonLeapWindow()
    {
        // A Feb-29 birthday projected onto a non-leap year clamps to Feb-28 (never throws).
        var today = new DateOnly(2027, 2, 25); // 2027 is not a leap year
        var dob = new DateOnly(2000, 2, 29);
        DashboardService.IsWithinNextDays(dob, today, 7, out var occ).Should().BeTrue();
        occ.Should().Be(new DateOnly(2027, 2, 28));
    }
}
