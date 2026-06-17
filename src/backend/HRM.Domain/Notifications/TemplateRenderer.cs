using System.Text;

namespace HRM.Domain.Notifications;

/// <summary>
/// Pure {{path.to.value}} placeholder substitution for email templates (US-NTF-002 FR-2/FR-4, BR-5). Stateless
/// and side-effect-free so it is trivially unit-testable and identical in preview (FR-4) and at send time.
///
/// <para>A placeholder is <c>{{ dotted.path }}</c> (whitespace around the path is ignored). The path is resolved
/// against a nested data dictionary (<c>Dictionary&lt;string, object?&gt;</c> nodes). An UNRESOLVED placeholder —
/// unknown path, a null value, or a path that walks into a non-dictionary — is replaced with the EMPTY STRING,
/// never the raw placeholder text (BR-5).</para>
/// </summary>
public static class TemplateRenderer
{
    /// <summary>
    /// Replaces every <c>{{path}}</c> in <paramref name="template"/> with the value resolved from
    /// <paramref name="data"/>, or "" if unresolved (BR-5). Returns <paramref name="template"/> unchanged when it
    /// contains no placeholders.
    /// </summary>
    public static string Render(string? template, IReadOnlyDictionary<string, object?> data)
    {
        if (string.IsNullOrEmpty(template))
            return template ?? string.Empty;

        var result = new StringBuilder(template.Length);
        var i = 0;

        while (i < template.Length)
        {
            var open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0)
            {
                result.Append(template, i, template.Length - i);
                break;
            }

            var close = template.IndexOf("}}", open + 2, StringComparison.Ordinal);
            if (close < 0)
            {
                // Unterminated placeholder — emit the remainder verbatim and stop.
                result.Append(template, i, template.Length - i);
                break;
            }

            // Emit the literal text before the placeholder.
            result.Append(template, i, open - i);

            var path = template.Substring(open + 2, close - (open + 2)).Trim();
            result.Append(Resolve(path, data));

            i = close + 2;
        }

        return result.ToString();
    }

    /// <summary>
    /// Walks the dotted <paramref name="path"/> through nested dictionaries in <paramref name="data"/>.
    /// Returns the leaf value as a string, or "" when the path cannot be resolved to a non-null leaf (BR-5).
    /// </summary>
    private static string Resolve(string path, IReadOnlyDictionary<string, object?> data)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        var segments = path.Split('.');
        object? current = data;

        foreach (var segment in segments)
        {
            if (current is IReadOnlyDictionary<string, object?> roDict)
            {
                if (!roDict.TryGetValue(segment, out current))
                    return string.Empty;
            }
            else if (current is IDictionary<string, object?> dict)
            {
                if (!dict.TryGetValue(segment, out current))
                    return string.Empty;
            }
            else
            {
                // Path walks into a non-dictionary node before reaching the leaf — unresolved (BR-5).
                return string.Empty;
            }

            if (current is null)
                return string.Empty;
        }

        // A path that stops on a dictionary (not a scalar leaf) is treated as unresolved.
        if (current is IReadOnlyDictionary<string, object?> || current is IDictionary<string, object?>)
            return string.Empty;

        return Convert.ToString(current, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
