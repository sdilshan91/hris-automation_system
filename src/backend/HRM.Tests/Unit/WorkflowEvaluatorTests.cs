// ============================================================================
// US-ADM-007: Pure WorkflowEvaluator + condition-language unit tests.
// Side-effect-free — no DB. Covers BR-5 (conditional skip), BR-3 (parallel,
// all-approvers-required), and each condition operator.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Workflows;

namespace HRM.Tests.Unit;

public sealed class WorkflowEvaluatorTests
{
    private static WorkflowStep Step(
        int order,
        WorkflowApproverType approverType = WorkflowApproverType.LineManager,
        string? condition = null,
        bool isParallel = false,
        int sla = 24) => new()
        {
            Id = BaseEntity.NewUuidV7(),
            StepOrder = order,
            ApproverType = approverType,
            ConditionJson = condition,
            IsParallel = isParallel,
            SlaHours = sla,
        };

    private static Dictionary<string, object?> Data(int days) => new() { ["days_requested"] = days };

    // ── BR-5: conditional skip ──────────────────────────────────────────────

    [Fact]
    public void Evaluate_3DayRequest_SkipsGreaterThan5Step()
    {
        var steps = new[]
        {
            Step(1), // Direct manager, always applies
            Step(2, WorkflowApproverType.DepartmentHead, condition: "{\"field\":\"days_requested\",\"operator\":\">\",\"value\":5}"),
            Step(3, WorkflowApproverType.Role, condition: "{\"field\":\"days_requested\",\"operator\":\">\",\"value\":15}"),
        };

        var applicable = WorkflowEvaluator.Evaluate(steps, Data(3));

        applicable.Should().HaveCount(1);
        applicable[0].StepOrder.Should().Be(1);
    }

    [Fact]
    public void Evaluate_10DayRequest_IncludesGreaterThan5StepButNot15()
    {
        var steps = new[]
        {
            Step(1),
            Step(2, WorkflowApproverType.DepartmentHead, condition: "{\"field\":\"days_requested\",\"operator\":\">\",\"value\":5}"),
            Step(3, WorkflowApproverType.Role, condition: "{\"field\":\"days_requested\",\"operator\":\">\",\"value\":15}"),
        };

        var applicable = WorkflowEvaluator.Evaluate(steps, Data(10));

        applicable.Select(s => s.StepOrder).Should().Equal(1, 2);
    }

    [Fact]
    public void Evaluate_20DayRequest_IncludesAllThreeSteps()
    {
        var steps = new[]
        {
            Step(1),
            Step(2, WorkflowApproverType.DepartmentHead, condition: "{\"field\":\"days_requested\",\"operator\":\">\",\"value\":5}"),
            Step(3, WorkflowApproverType.Role, condition: "{\"field\":\"days_requested\",\"operator\":\">\",\"value\":15}"),
        };

        var applicable = WorkflowEvaluator.Evaluate(steps, Data(20));

        applicable.Select(s => s.StepOrder).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Evaluate_OrdersStepsByStepOrder()
    {
        var steps = new[] { Step(3), Step(1), Step(2) };

        var applicable = WorkflowEvaluator.Evaluate(steps, Data(1));

        applicable.Select(s => s.StepOrder).Should().Equal(1, 2, 3);
    }

    // ── BR-3: parallel steps surfaced as all-approvers-required ─────────────

    [Fact]
    public void Evaluate_ParallelStep_SurfacedAsRequiringAllApprovers()
    {
        var steps = new[] { Step(1, isParallel: true) };

        var applicable = WorkflowEvaluator.Evaluate(steps, Data(1));

        applicable.Should().ContainSingle();
        applicable[0].IsParallel.Should().BeTrue();
        applicable[0].RequiresAllApprovers.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_SequentialStep_DoesNotRequireAllApprovers()
    {
        var steps = new[] { Step(1, isParallel: false) };

        var applicable = WorkflowEvaluator.Evaluate(steps, Data(1));

        applicable[0].RequiresAllApprovers.Should().BeFalse();
    }

    // ── Each condition operator (BR-5) ──────────────────────────────────────

    [Theory]
    [InlineData(">", 5, 6, true)]
    [InlineData(">", 5, 5, false)]
    [InlineData(">=", 5, 5, true)]
    [InlineData(">=", 5, 4, false)]
    [InlineData("<", 5, 4, true)]
    [InlineData("<", 5, 5, false)]
    [InlineData("<=", 5, 5, true)]
    [InlineData("<=", 5, 6, false)]
    [InlineData("==", 5, 5, true)]
    [InlineData("==", 5, 6, false)]
    [InlineData("!=", 5, 6, true)]
    [InlineData("!=", 5, 5, false)]
    public void Evaluate_NumericOperator_EvaluatesCorrectly(string op, int threshold, int actual, bool expectedApplies)
    {
        var condition = $"{{\"field\":\"days_requested\",\"operator\":\"{op}\",\"value\":{threshold}}}";
        var steps = new[] { Step(1, condition: condition) };

        var applicable = WorkflowEvaluator.Evaluate(steps, Data(actual));

        applicable.Should().HaveCount(expectedApplies ? 1 : 0);
    }

    [Fact]
    public void Evaluate_StringEqualityCondition_IsHonoured()
    {
        var steps = new[]
        {
            Step(1, condition: "{\"field\":\"department\",\"operator\":\"==\",\"value\":\"Engineering\"}"),
        };

        WorkflowEvaluator.Evaluate(steps, new Dictionary<string, object?> { ["department"] = "Engineering" })
            .Should().HaveCount(1);

        WorkflowEvaluator.Evaluate(steps, new Dictionary<string, object?> { ["department"] = "Sales" })
            .Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_MissingField_SkipsConditionalStep()
    {
        var steps = new[]
        {
            Step(1, condition: "{\"field\":\"days_requested\",\"operator\":\">\",\"value\":5}"),
        };

        WorkflowEvaluator.Evaluate(steps, new Dictionary<string, object?>())
            .Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_MalformedCondition_TreatedAsNotApplicable()
    {
        var steps = new[] { Step(1, condition: "{ not valid json") };

        WorkflowEvaluator.Evaluate(steps, Data(10)).Should().BeEmpty();
    }

    // ── WorkflowCondition.Validate (FR-5) ───────────────────────────────────

    [Fact]
    public void Validate_NullOrBlank_ReturnsNull()
    {
        WorkflowCondition.Validate(null).Should().BeNull();
        WorkflowCondition.Validate("").Should().BeNull();
        WorkflowCondition.Validate("   ").Should().BeNull();
    }

    [Fact]
    public void Validate_WellFormedCondition_ReturnsNull()
    {
        WorkflowCondition.Validate("{\"field\":\"days_requested\",\"operator\":\">\",\"value\":5}")
            .Should().BeNull();
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"operator\":\">\",\"value\":5}")] // missing field
    [InlineData("{\"field\":\"x\",\"value\":5}")] // missing operator
    [InlineData("{\"field\":\"x\",\"operator\":\">\"}")] // missing value
    [InlineData("{\"field\":\"x\",\"operator\":\"~=\",\"value\":5}")] // unsupported operator
    [InlineData("[1,2,3]")] // not an object
    public void Validate_MalformedCondition_ReturnsErrorMessage(string json)
    {
        WorkflowCondition.Validate(json).Should().NotBeNull();
    }
}
