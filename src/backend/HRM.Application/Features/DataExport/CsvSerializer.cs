using System.Globalization;
using System.Reflection;
using System.Text;

namespace HRM.Application.Features.DataExport;

/// <summary>
/// US-ADM-010 (FR-3): a PURE (no DB, no I/O) CSV serializer used by the export generation service. Produces
/// UTF-8 bytes WITH a BOM, a configurable single-char delimiter (default ','), a header row of column names, and
/// RFC-4180 field escaping. Kept pure so the export shape + auth-secret exclusion are unit-testable without a DB.
///
/// <para>Two modes:
/// <list type="bullet">
///   <item><b>Reflection mode</b> (<see cref="SerializeEntities{T}"/>): emits every PUBLIC INSTANCE scalar
///   property of <typeparamref name="T"/> in declaration order, EXCLUDING any whose name is on the auth-secret
///   deny-list (<see cref="ExportSensitiveFields"/>) — so password/MFA/token hashes can never appear (BR-7).</item>
///   <item><b>Explicit-column mode</b> (<see cref="Serialize"/>): the caller supplies the exact ordered columns
///   — used for the Users export, which projects ONLY name/email/roles (no credential columns at all).</item>
/// </list></para>
/// </summary>
public static class CsvSerializer
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>Serializes rows using the caller-supplied ordered columns. Each column has a header + a value selector.</summary>
    public static byte[] Serialize<T>(
        IReadOnlyList<T> rows,
        IReadOnlyList<(string Header, Func<T, object?> Value)> columns,
        char delimiter = ',',
        string? dateFormat = null)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(delimiter, columns.Select(c => Escape(c.Header, delimiter)))).Append('\n');

        foreach (var row in rows)
        {
            sb.Append(string.Join(
                    delimiter,
                    columns.Select(c => Escape(Format(c.Value(row), dateFormat), delimiter))))
              .Append('\n');
        }

        return WithBom(sb.ToString());
    }

    /// <summary>
    /// Reflection-based serialization of an entity list. Excludes auth-secret properties (BR-7) and any
    /// non-scalar (collection/complex) property. Property order follows the type's metadata token (declaration order).
    /// </summary>
    public static byte[] SerializeEntities<T>(
        IReadOnlyList<T> rows, char delimiter = ',', string? dateFormat = null)
    {
        var props = ExportableProperties(typeof(T));
        var columns = props
            .Select(p => (p.Name, (Func<T, object?>)(row => p.GetValue(row))))
            .ToList();
        return Serialize(rows, columns, delimiter, dateFormat);
    }

    /// <summary>
    /// The PUBLIC INSTANCE scalar properties of <paramref name="type"/> that are safe to export: not on the
    /// auth-secret deny-list, and a scalar/primitive type (collections + complex navigations are skipped).
    /// </summary>
    public static IReadOnlyList<PropertyInfo> ExportableProperties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => IsScalar(p.PropertyType))
            .Where(p => !ExportSensitiveFields.IsDenied(p.Name))
            .OrderBy(p => p.MetadataToken)
            .ToList();

    private static bool IsScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        if (t.IsEnum) return true;
        if (t == typeof(string) || t == typeof(Guid) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
            || t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(TimeSpan) || t == typeof(decimal))
            return true;
        return t.IsPrimitive; // bool/int/long/double/etc.
    }

    private static string Format(object? value, string? dateFormat)
    {
        return value switch
        {
            null => string.Empty,
            DateTime dt => dt.ToString(dateFormat ?? "O", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString(dateFormat ?? "O", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString(dateFormat ?? "yyyy-MM-dd", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>RFC-4180 escaping: wrap in quotes when the value contains the delimiter, a quote, or a newline.</summary>
    private static string Escape(string value, char delimiter)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var needsQuoting = value.IndexOf(delimiter) >= 0
            || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuoting)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static byte[] WithBom(string content)
    {
        var body = Encoding.UTF8.GetBytes(content);
        var result = new byte[Utf8Bom.Length + body.Length];
        Utf8Bom.CopyTo(result, 0);
        body.CopyTo(result, Utf8Bom.Length);
        return result;
    }
}
