// ============================================================================
// US-PAY-001: Safe salary-formula evaluator + circular-reference detector — unit tests.
// Covers the FR-4 safe expression subset, BR-6 syntax validation + circular references.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Payroll;

namespace HRM.Tests.Unit;

public sealed class SalaryFormulaTests
{
    // ── Evaluation ────────────────────────────────────────────────────

    [Theory]
    [InlineData("2 + 3", 5)]
    [InlineData("10 - 4", 6)]
    [InlineData("3 * 4", 12)]
    [InlineData("20 / 4", 5)]
    [InlineData("2 + 3 * 4", 14)]          // precedence
    [InlineData("(2 + 3) * 4", 20)]        // parentheses
    [InlineData("-5 + 8", 3)]              // unary minus
    [InlineData("100 * 0.12", 12)]         // decimals
    public void Evaluate_Arithmetic(string expr, decimal expected)
    {
        SalaryFormula.Evaluate(expr, new Dictionary<string, decimal>()).Should().Be(expected);
    }

    [Fact]
    public void Evaluate_WithVariables_IsCaseInsensitive()
    {
        var vars = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["basic"] = 1000m,
            ["hra"] = 400m,
        };

        SalaryFormula.Evaluate("basic * 0.12", vars).Should().Be(120m);
        SalaryFormula.Evaluate("(BASIC + Hra) * 0.1", vars).Should().Be(140m);
    }

    [Fact]
    public void Evaluate_DivisionByZero_Throws()
    {
        var act = () => SalaryFormula.Evaluate("basic / 0", new Dictionary<string, decimal> { ["basic"] = 100m });
        act.Should().Throw<SalaryFormula.FormulaException>().WithMessage("*Division by zero*");
    }

    [Fact]
    public void Evaluate_UnknownVariable_Throws()
    {
        var act = () => SalaryFormula.Evaluate("foo * 2", new Dictionary<string, decimal>());
        act.Should().Throw<SalaryFormula.FormulaException>().WithMessage("*Unknown variable*");
    }

    // ── Syntax validation (BR-6 syntax half) ──────────────────────────

    [Theory]
    [InlineData("basic * 0.12")]
    [InlineData("(basic + hra) / 2")]
    [InlineData("-basic + 100")]
    public void Validate_GoodExpressions_ReturnsNull(string expr)
    {
        SalaryFormula.Validate(expr).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("basic *")]          // trailing operator
    [InlineData("basic +* 2")]       // double operator
    [InlineData("(basic + 2")]       // unbalanced paren
    [InlineData("basic 2")]          // missing operator
    [InlineData("basic ; drop")]     // illegal character — no code execution surface
    [InlineData("pow(basic, 2)")]    // function calls are NOT in the grammar
    public void Validate_BadExpressions_ReturnsError(string expr)
    {
        SalaryFormula.Validate(expr).Should().NotBeNull();
    }

    [Fact]
    public void ReferencedVariables_ReturnsDistinctIdentifiers()
    {
        var vars = SalaryFormula.ReferencedVariables("(basic + hra) * basic - other");
        vars.Should().BeEquivalentTo(new[] { "basic", "hra", "other" });
    }

    // ── Circular-reference detection (BR-6) ───────────────────────────

    [Fact]
    public void FindCycle_Acyclic_ReturnsNull()
    {
        var deps = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["PF"] = new[] { "BASIC" },
            ["GROSS"] = new[] { "BASIC", "HRA" },
        };
        FormulaDependencyGraph.FindCycle(deps).Should().BeNull();
    }

    [Fact]
    public void FindCycle_DirectSelfReference_IsDetected()
    {
        var deps = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = new[] { "A" },
        };
        FormulaDependencyGraph.FindCycle(deps).Should().NotBeNull();
    }

    [Fact]
    public void FindCycle_MutualReference_IsDetected()
    {
        var deps = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = new[] { "B" },
            ["B"] = new[] { "A" },
        };
        var cycle = FormulaDependencyGraph.FindCycle(deps);
        cycle.Should().NotBeNull();
        cycle!.Should().Contain("A").And.Contain("B");
    }

    [Fact]
    public void FindCycle_EdgeToNonFormulaNode_IsLeaf_NoFalsePositive()
    {
        // "GROSS" references "BASIC" which is a fixed (non-formula) component, hence not in the graph.
        var deps = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GROSS"] = new[] { "BASIC" },
        };
        FormulaDependencyGraph.FindCycle(deps).Should().BeNull();
    }
}
