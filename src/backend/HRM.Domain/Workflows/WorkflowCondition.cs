using System.Text.Json;

namespace HRM.Domain.Workflows;

/// <summary>
/// The simple condition language a workflow step may carry (US-ADM-007 FR-2/BR-5): a single
/// <c>{field, operator, value}</c> comparison. Complex AND/OR groups are deferred to Phase 2 (story §10).
///
/// <para>Side-effect-free: parsing and evaluation never touch the database or any service. This is the pure
/// core of the engine — the runtime invocation from live request flows is the deferred part.</para>
/// </summary>
public sealed record WorkflowCondition(string Field, string Operator, JsonElement Value)
{
    /// <summary>The supported comparison operators (BR-5).</summary>
    public static readonly IReadOnlySet<string> SupportedOperators =
        new HashSet<string>(StringComparer.Ordinal) { ">", ">=", "<", "<=", "==", "!=" };

    /// <summary>
    /// Parses a condition JSON string. Returns null for null/blank input (= no condition, always applicable).
    /// Throws <see cref="FormatException"/> for malformed JSON / missing keys / unsupported operator, so the
    /// validator can surface a precise message.
    /// </summary>
    public static WorkflowCondition? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Condition is not valid JSON: {ex.Message}", ex);
        }

        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("Condition must be a JSON object with field/operator/value.");

        if (!root.TryGetProperty("field", out var fieldEl) || fieldEl.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(fieldEl.GetString()))
            throw new FormatException("Condition is missing a non-empty string 'field'.");

        if (!root.TryGetProperty("operator", out var opEl) || opEl.ValueKind != JsonValueKind.String)
            throw new FormatException("Condition is missing a string 'operator'.");

        var op = opEl.GetString()!;
        if (!SupportedOperators.Contains(op))
            throw new FormatException(
                $"Unsupported condition operator '{op}'. Supported: {string.Join(", ", SupportedOperators)}.");

        if (!root.TryGetProperty("value", out var valueEl))
            throw new FormatException("Condition is missing a 'value'.");

        return new WorkflowCondition(fieldEl.GetString()!, op, valueEl.Clone());
    }

    /// <summary>
    /// Validates that <paramref name="json"/> is a well-formed condition (FR-5). Returns null when valid (or
    /// blank), otherwise a human-readable error message.
    /// </summary>
    public static string? Validate(string? json)
    {
        try
        {
            Parse(json);
            return null;
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Evaluates this condition against a request's data dictionary (BR-5). A missing field, or a value that
    /// cannot be compared, yields <c>false</c> (the step is treated as not-applicable / skipped). Numeric and
    /// string comparisons are both supported; ==/!= also work for booleans.
    /// </summary>
    public bool IsSatisfiedBy(IReadOnlyDictionary<string, object?> data)
    {
        if (data is null || !data.TryGetValue(Field, out var actual))
            return false;

        // Prefer numeric comparison when both sides are numbers.
        if (TryGetNumber(actual, out var actualNum) && TryGetNumber(Value, out var expectedNum))
            return CompareNumbers(actualNum, expectedNum);

        // Equality operators additionally support string/bool comparison.
        if (Operator is "==" or "!=")
        {
            var equal = StringEquals(actual, Value);
            return Operator == "==" ? equal : !equal;
        }

        // Ordering operators on non-numeric values: fall back to ordinal string comparison.
        var cmp = string.CompareOrdinal(Stringify(actual), Stringify(Value));
        return Operator switch
        {
            ">" => cmp > 0,
            ">=" => cmp >= 0,
            "<" => cmp < 0,
            "<=" => cmp <= 0,
            _ => false,
        };
    }

    private bool CompareNumbers(double actual, double expected) => Operator switch
    {
        ">" => actual > expected,
        ">=" => actual >= expected,
        "<" => actual < expected,
        "<=" => actual <= expected,
        "==" => actual == expected,
        "!=" => actual != expected,
        _ => false,
    };

    private static bool TryGetNumber(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case JsonElement el when el.ValueKind == JsonValueKind.Number:
                return el.TryGetDouble(out number);
            case JsonElement el when el.ValueKind == JsonValueKind.String:
                return double.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture, out number);
            case JsonElement:
                number = 0;
                return false;
            case string s:
                return double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out number);
            default:
                try
                {
                    number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    number = 0;
                    return false;
                }
        }
    }

    private static bool StringEquals(object? actual, object? expected)
        => string.Equals(Stringify(actual), Stringify(expected), StringComparison.OrdinalIgnoreCase);

    private static string Stringify(object? value) => value switch
    {
        null => string.Empty,
        JsonElement el when el.ValueKind == JsonValueKind.String => el.GetString() ?? string.Empty,
        JsonElement el when el.ValueKind == JsonValueKind.True => "true",
        JsonElement el when el.ValueKind == JsonValueKind.False => "false",
        JsonElement el => el.GetRawText(),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
