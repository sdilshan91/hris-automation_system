using System.Globalization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Domain.Benefits;

/// <summary>
/// A snapshot of the Core HR attributes an eligibility rule can test (US-TRN-003 FR-4). Pure input to
/// <see cref="BenefitEligibilityEvaluator"/> — no DB access. <see cref="TenureDays"/> is days since the employee's
/// join date; <see cref="JobTitleId"/> backs the "JobGrade" attribute (no separate grade column).
/// </summary>
public readonly record struct EmployeeAttributes(
    EmploymentType EmploymentType,
    int TenureDays,
    Guid DepartmentId,
    Guid JobTitleId);

/// <summary>
/// Pure, DB-free evaluator for benefit-plan eligibility (US-TRN-003 FR-4, BR-1). All of a plan's
/// <see cref="BenefitEligibilityRule"/> rows are ANDed against an <see cref="EmployeeAttributes"/> snapshot: the
/// employee is eligible only if every rule passes. No rules → eligible (a plan with no rules is open to all active
/// employees, AC-1). On the first failing rule the evaluator returns a specific reason (AC-4/BR-4). A malformed rule
/// value is treated defensively as NOT satisfied (fail-closed) rather than throwing.
/// </summary>
public static class BenefitEligibilityEvaluator
{
    private static readonly string[] NumericOperators = ["==", "!=", ">", ">=", "<", "<="];

    /// <summary>
    /// Evaluates <paramref name="rules"/> (ANDed) against <paramref name="emp"/>. Returns
    /// <c>(true, null)</c> when eligible, or <c>(false, reason)</c> naming the first failing rule.
    /// </summary>
    public static (bool Eligible, string? FailedReason) Evaluate(
        IEnumerable<BenefitEligibilityRule> rules, EmployeeAttributes emp)
    {
        foreach (var rule in rules)
        {
            if (!RulePasses(rule, emp))
                return (false, $"Not eligible: {rule.Attribute} {rule.Operator} {rule.Value} not satisfied");
        }

        return (true, null);
    }

    /// <summary>Evaluates a single rule against the employee snapshot. Fail-closed on any malformed value.</summary>
    private static bool RulePasses(BenefitEligibilityRule rule, EmployeeAttributes emp) => rule.Attribute switch
    {
        EligibilityAttribute.EmploymentType => EvaluateEmploymentType(rule.Operator, rule.Value, emp.EmploymentType),
        EligibilityAttribute.TenureDays => EvaluateNumeric(rule.Operator, rule.Value, emp.TenureDays),
        EligibilityAttribute.Department => EvaluateGuid(rule.Operator, rule.Value, emp.DepartmentId),
        EligibilityAttribute.JobGrade => EvaluateGuid(rule.Operator, rule.Value, emp.JobTitleId),
        _ => false,
    };

    private static bool EvaluateEmploymentType(string op, string value, EmploymentType actual)
    {
        if (!Enum.TryParse<EmploymentType>(value?.Trim(), ignoreCase: true, out var expected))
            return false; // malformed → fail-closed
        return op switch
        {
            "==" => actual == expected,
            "!=" => actual != expected,
            _ => false, // only equality operators are legal for an enum attribute
        };
    }

    private static bool EvaluateNumeric(string op, string value, int actual)
    {
        if (!int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var expected))
            return false; // malformed → fail-closed
        return op switch
        {
            "==" => actual == expected,
            "!=" => actual != expected,
            ">" => actual > expected,
            ">=" => actual >= expected,
            "<" => actual < expected,
            "<=" => actual <= expected,
            _ => false,
        };
    }

    private static bool EvaluateGuid(string op, string value, Guid actual)
    {
        if (op == "In")
        {
            var set = ParseGuidList(value);
            return set.Count > 0 && set.Contains(actual);
        }

        if (!Guid.TryParse(value?.Trim(), out var expected))
            return false; // malformed → fail-closed
        return op switch
        {
            "==" => actual == expected,
            "!=" => actual != expected,
            _ => false,
        };
    }

    private static List<Guid> ParseGuidList(string value)
    {
        var result = new List<Guid>();
        if (string.IsNullOrWhiteSpace(value))
            return result;
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Guid.TryParse(part, out var g))
                result.Add(g);
            else
                return []; // any malformed member → fail-closed (empty set never matches)
        }

        return result;
    }

    // ── Rule authoring validation (US-TRN-003 rule CRUD, 400 invalid_rule) ───────────────────────────────────────

    /// <summary>
    /// Validates that <paramref name="attribute"/>, <paramref name="op"/> and <paramref name="value"/> form a legal
    /// rule for the given attribute type — the operator is legal for that attribute and the value parses. Returns an
    /// error message (for a 400 <c>invalid_rule</c>) or <c>null</c> when the rule is well-formed.
    /// </summary>
    public static string? ValidateRule(EligibilityAttribute attribute, string op, string value)
    {
        op = (op ?? string.Empty).Trim();
        value = value?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(value))
            return "Rule value is required.";

        switch (attribute)
        {
            case EligibilityAttribute.EmploymentType:
                if (op is not ("==" or "!="))
                    return $"Operator '{op}' is not valid for EmploymentType (use == or !=).";
                if (!Enum.TryParse<EmploymentType>(value, ignoreCase: true, out _))
                    return $"'{value}' is not a valid employment type.";
                return null;

            case EligibilityAttribute.TenureDays:
                if (!NumericOperators.Contains(op))
                    return $"Operator '{op}' is not valid for TenureDays.";
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    return $"'{value}' is not a valid number of days.";
                return null;

            case EligibilityAttribute.Department:
            case EligibilityAttribute.JobGrade:
                if (op is not ("==" or "!=" or "In"))
                    return $"Operator '{op}' is not valid for {attribute} (use ==, != or In).";
                if (op == "In")
                {
                    var list = ParseGuidList(value);
                    if (list.Count == 0)
                        return "The 'In' list must contain at least one valid id.";
                }
                else if (!Guid.TryParse(value, out _))
                {
                    return $"'{value}' is not a valid id.";
                }

                return null;

            default:
                return "Unknown eligibility attribute.";
        }
    }
}
