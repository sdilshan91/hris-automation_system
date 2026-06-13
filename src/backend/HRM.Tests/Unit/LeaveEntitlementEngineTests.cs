// ============================================================================
// US-LV-002: Leave Entitlement Engine Unit Tests
// Tests the pure rule-priority/specificity engine and pro-rata calculation
// without any I/O dependencies (AC-2, AC-4, BR-2, BR-4).
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Services;

namespace HRM.Tests.Unit;

public sealed class LeaveEntitlementEngineTests
{
    private static readonly Guid DeptEngineering = Guid.NewGuid();
    private static readonly Guid DeptSales = Guid.NewGuid();
    private static readonly Guid JtSeniorDev = Guid.NewGuid();
    private static readonly Guid JtJuniorDev = Guid.NewGuid();
    private static readonly Guid LeaveTypeId = Guid.NewGuid();

    // ══════════════════════════════════════════════════════════════
    //  Rule Priority / Specificity (AC-2)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_MostSpecificRule_DeptPlusJobTitlePlusEmploymentType_Wins()
    {
        // Arrange: three rules with different specificity.
        var rules = new List<LeaveEntitlementRule>
        {
            MakeRule(departmentId: DeptEngineering, entitlementDays: 20, priority: 1),               // dept only (score 4)
            MakeRule(departmentId: DeptEngineering, jobTitleId: JtSeniorDev, entitlementDays: 25, priority: 1), // dept+title (score 6)
            MakeRule(departmentId: DeptEngineering, jobTitleId: JtSeniorDev,
                     employmentType: EmploymentType.FullTime, entitlementDays: 30, priority: 1),     // dept+title+type (score 7)
        };

        // Act
        var result = LeaveEntitlementEngine.Resolve(
            rules, DeptEngineering, JtSeniorDev, EmploymentType.FullTime,
            tenureMonths: 12, leaveTypeDefaultEntitlement: 14);

        // Assert: most specific rule wins (dept+title+type = 30 days).
        result.BaseEntitlementDays.Should().Be(30);
        result.Source.Should().StartWith("rule:");
    }

    [Fact]
    public void Resolve_DeptPlusJobTitle_BeatsJobTitleOnly()
    {
        var rules = new List<LeaveEntitlementRule>
        {
            MakeRule(jobTitleId: JtSeniorDev, entitlementDays: 22, priority: 1),                         // title only (score 2)
            MakeRule(departmentId: DeptEngineering, jobTitleId: JtSeniorDev, entitlementDays: 25, priority: 1), // dept+title (score 6)
        };

        var result = LeaveEntitlementEngine.Resolve(
            rules, DeptEngineering, JtSeniorDev, EmploymentType.FullTime,
            tenureMonths: 0, leaveTypeDefaultEntitlement: 14);

        result.BaseEntitlementDays.Should().Be(25);
    }

    [Fact]
    public void Resolve_DeptOnly_BeatsJobTitleOnly()
    {
        var rules = new List<LeaveEntitlementRule>
        {
            MakeRule(departmentId: DeptEngineering, entitlementDays: 20, priority: 1),  // dept only (score 4)
            MakeRule(jobTitleId: JtSeniorDev, entitlementDays: 18, priority: 1),        // title only (score 2)
        };

        var result = LeaveEntitlementEngine.Resolve(
            rules, DeptEngineering, JtSeniorDev, EmploymentType.FullTime,
            tenureMonths: 0, leaveTypeDefaultEntitlement: 14);

        result.BaseEntitlementDays.Should().Be(20);
    }

    [Fact]
    public void Resolve_SameSpecificity_LowerPriorityNumber_Wins()
    {
        var rules = new List<LeaveEntitlementRule>
        {
            MakeRule(departmentId: DeptEngineering, entitlementDays: 20, priority: 5),
            MakeRule(departmentId: DeptEngineering, entitlementDays: 25, priority: 1), // lower number = higher priority
        };

        var result = LeaveEntitlementEngine.Resolve(
            rules, DeptEngineering, JtSeniorDev, EmploymentType.FullTime,
            tenureMonths: 0, leaveTypeDefaultEntitlement: 14);

        result.BaseEntitlementDays.Should().Be(25);
    }

    [Fact]
    public void Resolve_NoMatchingRules_FallsBackToLeaveTypeDefault()
    {
        var rules = new List<LeaveEntitlementRule>
        {
            MakeRule(departmentId: DeptSales, entitlementDays: 20, priority: 1), // wrong dept
        };

        var result = LeaveEntitlementEngine.Resolve(
            rules, DeptEngineering, JtSeniorDev, EmploymentType.FullTime,
            tenureMonths: 0, leaveTypeDefaultEntitlement: 14);

        result.BaseEntitlementDays.Should().Be(14);
        result.Source.Should().Be("leave_type_default");
    }

    [Fact]
    public void Resolve_EmptyRules_FallsBackToDefault()
    {
        var result = LeaveEntitlementEngine.Resolve(
            new List<LeaveEntitlementRule>(), DeptEngineering, JtSeniorDev,
            EmploymentType.FullTime, tenureMonths: 0, leaveTypeDefaultEntitlement: 14);

        result.BaseEntitlementDays.Should().Be(14);
        result.Source.Should().Be("leave_type_default");
    }

    [Fact]
    public void Resolve_EmploymentTypeMismatch_RuleExcluded()
    {
        var rules = new List<LeaveEntitlementRule>
        {
            MakeRule(departmentId: DeptEngineering, employmentType: EmploymentType.PartTime,
                     entitlementDays: 10, priority: 1),
        };

        var result = LeaveEntitlementEngine.Resolve(
            rules, DeptEngineering, JtSeniorDev, EmploymentType.FullTime,
            tenureMonths: 0, leaveTypeDefaultEntitlement: 14);

        // Should fall back to default since employment type doesn't match.
        result.BaseEntitlementDays.Should().Be(14);
        result.Source.Should().Be("leave_type_default");
    }

    // ── Tenure brackets ────────────────────────────────────────────

    [Fact]
    public void Resolve_TenureBracket_Matches()
    {
        var rules = new List<LeaveEntitlementRule>
        {
            MakeRule(departmentId: DeptEngineering, tenureMin: 0, tenureMax: 12, entitlementDays: 15, priority: 1),
            MakeRule(departmentId: DeptEngineering, tenureMin: 12, tenureMax: 60, entitlementDays: 20, priority: 1),
            MakeRule(departmentId: DeptEngineering, tenureMin: 60, tenureMax: null, entitlementDays: 25, priority: 1),
        };

        var result24m = LeaveEntitlementEngine.Resolve(
            rules, DeptEngineering, JtSeniorDev, EmploymentType.FullTime,
            tenureMonths: 24, leaveTypeDefaultEntitlement: 14);

        result24m.BaseEntitlementDays.Should().Be(20);

        var result6m = LeaveEntitlementEngine.Resolve(
            rules, DeptEngineering, JtSeniorDev, EmploymentType.FullTime,
            tenureMonths: 6, leaveTypeDefaultEntitlement: 14);

        result6m.BaseEntitlementDays.Should().Be(15);
    }

    [Fact]
    public void Resolve_TenureMinExclusive_BoundaryCheck()
    {
        // tenureMax is exclusive: 12 means < 12 months tenure.
        var rules = new List<LeaveEntitlementRule>
        {
            MakeRule(departmentId: DeptEngineering, tenureMin: 0, tenureMax: 12, entitlementDays: 15, priority: 1),
        };

        // At exactly 12 months, should NOT match (tenureMax is exclusive).
        var result = LeaveEntitlementEngine.Resolve(
            rules, DeptEngineering, JtSeniorDev, EmploymentType.FullTime,
            tenureMonths: 12, leaveTypeDefaultEntitlement: 14);

        result.BaseEntitlementDays.Should().Be(14); // fallback
    }

    // ══════════════════════════════════════════════════════════════
    //  Pro-Rata Calculation (AC-4, BR-2)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void ProRata_JoinJuly1_20DayAnnual_Returns10()
    {
        // Test hint from US-LV-002: join Jul 1, 20-day annual -> 10 days.
        var result = LeaveEntitlementEngine.CalculateProRata(
            fullYearEntitlement: 20,
            dateOfJoining: new DateTime(2026, 7, 1),
            leaveYear: 2026,
            fte: 1.0m);

        result.Should().Be(10.08m); // 184/365 * 20 = 10.0822 -> 10.08 (half-up 2dp)
    }

    [Fact]
    public void ProRata_JoinJan1_FullEntitlement()
    {
        var result = LeaveEntitlementEngine.CalculateProRata(
            fullYearEntitlement: 20,
            dateOfJoining: new DateTime(2026, 1, 1),
            leaveYear: 2026);

        result.Should().Be(20m);
    }

    [Fact]
    public void ProRata_JoinPreviousYear_FullEntitlement()
    {
        var result = LeaveEntitlementEngine.CalculateProRata(
            fullYearEntitlement: 20,
            dateOfJoining: new DateTime(2020, 3, 15),
            leaveYear: 2026);

        result.Should().Be(20m);
    }

    [Fact]
    public void ProRata_JoinDec31_MinimalEntitlement()
    {
        var result = LeaveEntitlementEngine.CalculateProRata(
            fullYearEntitlement: 20,
            dateOfJoining: new DateTime(2026, 12, 31),
            leaveYear: 2026);

        // 1/365 * 20 = 0.0548 -> 0.05
        result.Should().Be(0.05m);
    }

    [Fact]
    public void ProRata_JoinNextYear_ReturnsZero()
    {
        var result = LeaveEntitlementEngine.CalculateProRata(
            fullYearEntitlement: 20,
            dateOfJoining: new DateTime(2027, 3, 1),
            leaveYear: 2026);

        result.Should().Be(0m);
    }

    [Fact]
    public void ProRata_NeverNegative_BR4()
    {
        // Even with 0 entitlement, result should be 0, not negative.
        var result = LeaveEntitlementEngine.CalculateProRata(
            fullYearEntitlement: 0,
            dateOfJoining: new DateTime(2026, 7, 1),
            leaveYear: 2026);

        result.Should().Be(0m);
    }

    [Fact]
    public void ProRata_PartTimeFte_HalvesEntitlement()
    {
        // FTE 0.5 with 20-day annual -> 10 * ratio.
        var result = LeaveEntitlementEngine.CalculateProRata(
            fullYearEntitlement: 20,
            dateOfJoining: new DateTime(2026, 1, 1),
            leaveYear: 2026,
            fte: 0.5m);

        result.Should().Be(10m);
    }

    [Fact]
    public void ProRata_Rounds_HalfUp()
    {
        // 15 days, join April 1 -> 275/365 * 15 = 11.30137 -> 11.30
        var result = LeaveEntitlementEngine.CalculateProRata(
            fullYearEntitlement: 15,
            dateOfJoining: new DateTime(2026, 4, 1),
            leaveYear: 2026);

        result.Should().Be(11.30m);
    }

    // ══════════════════════════════════════════════════════════════
    //  Tenure Calculation
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("2020-01-01", "2026-01-01", 72)]    // Exactly 6 years
    [InlineData("2025-06-15", "2026-01-01", 6)]      // 6 months, 17 days
    [InlineData("2026-01-01", "2026-01-01", 0)]       // Same day
    [InlineData("2026-02-01", "2026-01-01", 0)]       // Future joining
    [InlineData("2025-01-15", "2026-01-10", 11)]      // Day not reached yet
    public void ComputeTenureMonths_VariousCases(string joinStr, string refStr, int expected)
    {
        var join = DateTime.Parse(joinStr);
        var refDate = DateTime.Parse(refStr);

        var result = LeaveEntitlementEngine.ComputeTenureMonths(join, refDate);

        result.Should().Be(expected);
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private static LeaveEntitlementRule MakeRule(
        Guid? departmentId = null,
        Guid? jobTitleId = null,
        EmploymentType? employmentType = null,
        int? tenureMin = null,
        int? tenureMax = null,
        decimal entitlementDays = 14,
        int priority = 1)
    {
        return new LeaveEntitlementRule
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            LeaveTypeId = LeaveTypeId,
            DepartmentId = departmentId,
            JobTitleId = jobTitleId,
            EmploymentType = employmentType,
            TenureMinMonths = tenureMin,
            TenureMaxMonths = tenureMax,
            EntitlementDays = entitlementDays,
            Priority = priority,
            EffectiveFrom = new DateTime(2025, 1, 1),
            IsActive = true,
        };
    }
}
