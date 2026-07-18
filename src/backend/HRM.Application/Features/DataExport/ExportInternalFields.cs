namespace HRM.Application.Features.DataExport;

/// <summary>
/// US-ADM-010 (FR-3 — ISSUE-014): the internal/infrastructure-column deny-list applied by the reflection-mode
/// CSV serializer. Unlike <see cref="ExportSensitiveFields"/> (which strips auth SECRETS), this strips the
/// persistence/audit plumbing inherited from <c>BaseEntity</c> (tenant discriminator, audit actor/timestamps,
/// soft-delete flag, concurrency token) that is not part of the tenant's business data, so the portability
/// artifact's header row matches the business schema instead of leaking `TenantId`/`CreatedBy`/`RowVersion`/etc.
///
/// <para>The primary key (<c>Id</c>) is deliberately RETAINED — it is a stable join key linking the per-entity
/// CSVs (e.g. an employee's <c>Id</c> is referenced by <c>DepartmentId</c>/<c>ReportsToEmployeeId</c> elsewhere).
/// Matching is on the NORMALIZED property name (lower-cased, non-alphanumerics stripped) using EXACT equality
/// (not substring) so a business field is never dropped by accident.</para>
/// </summary>
public static class ExportInternalFields
{
    /// <summary>Normalized property names that identify an internal/infra column excluded from exports.</summary>
    private static readonly HashSet<string> InternalNames = new(StringComparer.Ordinal)
    {
        "tenantid",      // tenant discriminator (foreign-tenant plumbing)
        "createdat",     // audit timestamp
        "createdby",     // audit actor
        "updatedat",     // audit timestamp
        "updatedby",     // audit actor
        "isdeleted",     // soft-delete flag
        "rowversion",    // concurrency token (uint xmin)
        "version",       // concurrency token (alternate name on some entities)
    };

    /// <summary>True iff a property with this name is internal persistence/audit plumbing (exclude from exports).</summary>
    public static bool IsInternal(string propertyName)
        => InternalNames.Contains(Normalize(propertyName));

    private static string Normalize(string name)
    {
        Span<char> buffer = name.Length <= 128 ? stackalloc char[name.Length] : new char[name.Length];
        var len = 0;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
                buffer[len++] = char.ToLowerInvariant(c);
        }
        return new string(buffer[..len]);
    }
}
