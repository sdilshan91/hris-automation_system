using System.Text;

namespace HRM.Application.Common.Helpers;

/// <summary>
/// Shared UTF-8 CSV primitives so every report exporter emits a byte-identical CSV envelope.
///
/// <para>ISSUE-198: the HR report renderer prepended a UTF-8 BOM (so Excel auto-detects UTF-8 and renders
/// accented names correctly) while the payroll and leave CSV writers omitted it — an export-fidelity
/// inconsistency across modules. All three now write the BOM via <see cref="WriteBom"/> and use the
/// no-BOM <see cref="NoBomEncoding"/> for the <see cref="StreamWriter"/> so exactly ONE leading BOM is
/// emitted (a StreamWriter with a BOM-emitting encoding would add a second).</para>
/// </summary>
public static class CsvExport
{
    /// <summary>The UTF-8 byte-order mark (EF BB BF) Excel uses to auto-detect UTF-8 on CSV open.</summary>
    public static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// A UTF-8 encoding that does NOT emit a BOM, so the caller controls the single leading BOM itself
    /// (via <see cref="WriteBom"/>) rather than having the <see cref="StreamWriter"/> add its own.
    /// </summary>
    public static readonly UTF8Encoding NoBomEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes the leading UTF-8 BOM (EF BB BF) to <paramref name="stream"/>.</summary>
    public static void WriteBom(Stream stream) => stream.Write(Utf8Bom, 0, 3);
}
