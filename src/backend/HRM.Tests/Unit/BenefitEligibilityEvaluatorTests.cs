// ============================================================================
// US-TRN-003 — pure eligibility evaluator matrix (BenefitEligibilityEvaluator). No DB: rules are ANDed against an
// EmployeeAttributes snapshot per the typed per-attribute comparison rules. Also covers the rule-authoring
// validation (ValidateRule) used to reject malformed rules at create-time (400 invalid_rule).
// ============================================================================

using FluentAssertions;
using HRM.Domain.Benefits;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using Xunit;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-TRN-003")]
public sealed class BenefitEligibilityEvaluatorTests
{
    private static readonly Guid DeptEng = Guid.NewGuid();
    private static readonly Guid DeptSales = Guid.NewGuid();
    private static readonly Guid GradeSenior = Guid.NewGuid();
    private static readonly Guid GradeJunior = Guid.NewGuid();

    private static EmployeeAttributes FullTimeEng(int tenureDays = 200) =>
        new(EmploymentType.FullTime, tenureDays, DeptEng, GradeSenior);

    private static BenefitEligibilityRule Rule(EligibilityAttribute attr, string op, string value) =>
        new() { Attribute = attr, Operator = op, Value = value };

    // ── No rules → eligible (AC-1) ───────────────────────────────────────────────

    [Fact]
    public void NoRules_IsEligible()
    {
        var (eligible, reason) = BenefitEligibilityEvaluator.Evaluate([], FullTimeEng());
        eligible.Should().BeTrue();
        reason.Should().BeNull();
    }

    // ── EmploymentType ==/!= ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("==", "FullTime", true)]
    [InlineData("==", "Contract", false)]
    [InlineData("!=", "Contract", true)]
    [InlineData("!=", "FullTime", false)]
    [InlineData("==", "fulltime", true)]   // case-insensitive
    public void EmploymentType_EqualityOperators(string op, string value, bool expected)
    {
        var rules = new[] { Rule(EligibilityAttribute.EmploymentType, op, value) };
        BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng()).Eligible.Should().Be(expected);
    }

    [Fact]
    public void EmploymentType_UnsupportedOperator_NotEligible()
    {
        var rules = new[] { Rule(EligibilityAttribute.EmploymentType, ">", "FullTime") };
        BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng()).Eligible.Should().BeFalse();
    }

    // ── TenureDays >/>=/</<= ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(">", "90", 200, true)]
    [InlineData(">", "200", 200, false)]
    [InlineData(">=", "200", 200, true)]
    [InlineData("<", "90", 30, true)]
    [InlineData("<=", "30", 30, true)]
    [InlineData("<", "30", 30, false)]
    [InlineData("==", "200", 200, true)]
    [InlineData("!=", "200", 200, false)]
    public void TenureDays_NumericOperators(string op, string value, int tenure, bool expected)
    {
        var rules = new[] { Rule(EligibilityAttribute.TenureDays, op, value) };
        BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng(tenure)).Eligible.Should().Be(expected);
    }

    // ── Department ==/In ─────────────────────────────────────────────────────────

    [Fact]
    public void Department_Equality_Match()
    {
        var rules = new[] { Rule(EligibilityAttribute.Department, "==", DeptEng.ToString()) };
        BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng()).Eligible.Should().BeTrue();
    }

    [Fact]
    public void Department_Equality_NoMatch()
    {
        var rules = new[] { Rule(EligibilityAttribute.Department, "==", DeptSales.ToString()) };
        BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng()).Eligible.Should().BeFalse();
    }

    [Fact]
    public void Department_In_Membership()
    {
        var value = $"{DeptSales}, {DeptEng}";
        var rules = new[] { Rule(EligibilityAttribute.Department, "In", value) };
        BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng()).Eligible.Should().BeTrue();
    }

    [Fact]
    public void Department_In_NotMember()
    {
        var value = $"{DeptSales}, {Guid.NewGuid()}";
        var rules = new[] { Rule(EligibilityAttribute.Department, "In", value) };
        BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng()).Eligible.Should().BeFalse();
    }

    // ── JobGrade ==/In (maps to JobTitleId) ──────────────────────────────────────

    [Fact]
    public void JobGrade_Equality_Match()
    {
        var rules = new[] { Rule(EligibilityAttribute.JobGrade, "==", GradeSenior.ToString()) };
        BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng()).Eligible.Should().BeTrue();
    }

    [Fact]
    public void JobGrade_In_Membership()
    {
        var value = $"{GradeJunior}, {GradeSenior}";
        var rules = new[] { Rule(EligibilityAttribute.JobGrade, "In", value) };
        BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng()).Eligible.Should().BeTrue();
    }

    // ── AND of multiple rules ────────────────────────────────────────────────────

    [Fact]
    public void MultipleRules_AllPass_Eligible()
    {
        var rules = new[]
        {
            Rule(EligibilityAttribute.EmploymentType, "==", "FullTime"),
            Rule(EligibilityAttribute.TenureDays, ">=", "90"),
            Rule(EligibilityAttribute.Department, "==", DeptEng.ToString()),
        };
        BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng()).Eligible.Should().BeTrue();
    }

    [Fact]
    public void MultipleRules_OneFails_NotEligible_WithFailingReason()
    {
        var rules = new[]
        {
            Rule(EligibilityAttribute.EmploymentType, "==", "FullTime"),
            Rule(EligibilityAttribute.TenureDays, ">=", "365"), // fails: tenure 200 < 365
        };
        var (eligible, reason) = BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng());
        eligible.Should().BeFalse();
        reason.Should().Contain("TenureDays").And.Contain("365");
    }

    // ── Malformed rule value → not eligible (fail-closed) ────────────────────────

    [Theory]
    [InlineData(EligibilityAttribute.TenureDays, ">", "not-a-number")]
    [InlineData(EligibilityAttribute.EmploymentType, "==", "NotAType")]
    [InlineData(EligibilityAttribute.Department, "==", "not-a-guid")]
    public void MalformedValue_NotEligible(EligibilityAttribute attr, string op, string value)
    {
        var rules = new[] { Rule(attr, op, value) };
        BenefitEligibilityEvaluator.Evaluate(rules, FullTimeEng()).Eligible.Should().BeFalse();
    }

    // ── ValidateRule (rule authoring, 400 invalid_rule) ──────────────────────────

    [Theory]
    [InlineData(EligibilityAttribute.EmploymentType, "==", "FullTime")]
    [InlineData(EligibilityAttribute.TenureDays, ">=", "90")]
    [InlineData(EligibilityAttribute.Department, "In", "a,b")] // guid list checked below separately
    public void ValidateRule_WellFormed_ReturnsNull_OrGuidError(EligibilityAttribute attr, string op, string value)
    {
        // For the Department "In" case, "a,b" are not guids → should be rejected. Use real guids to pass.
        if (attr == EligibilityAttribute.Department)
            value = $"{Guid.NewGuid()},{Guid.NewGuid()}";
        BenefitEligibilityEvaluator.ValidateRule(attr, op, value).Should().BeNull();
    }

    [Theory]
    [InlineData(EligibilityAttribute.EmploymentType, ">", "FullTime")]  // illegal operator
    [InlineData(EligibilityAttribute.EmploymentType, "==", "Nope")]     // bad enum
    [InlineData(EligibilityAttribute.TenureDays, "In", "90")]           // illegal operator
    [InlineData(EligibilityAttribute.TenureDays, ">", "abc")]           // bad int
    [InlineData(EligibilityAttribute.Department, ">", "x")]             // illegal operator
    [InlineData(EligibilityAttribute.JobGrade, "==", "not-a-guid")]     // bad guid
    public void ValidateRule_Malformed_ReturnsError(EligibilityAttribute attr, string op, string value)
    {
        BenefitEligibilityEvaluator.ValidateRule(attr, op, value).Should().NotBeNull();
    }
}
