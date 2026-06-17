using HRM.Application.Common.Models;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// The template RESOLUTION + RENDERING seam other modules call to turn a catalog event + data into a ready-to-send
/// email (US-NTF-002 FR-2/FR-4/FR-6, BR-2/BR-5). Resolution applies the override→default + language-fallback rules;
/// rendering merges {{placeholders}} from a nested data dictionary (unresolved → "").
///
/// <para>Tenant isolation: <see cref="ResolveAsync"/> resolves the tenant override via the EF global query filter
/// (current tenant) and falls back to the non-tenant-scoped system default — so it must run within a resolved
/// tenant context.</para>
/// </summary>
public interface IEmailTemplateService
{
    /// <summary>
    /// Resolves the EFFECTIVE template for (<paramref name="eventKey"/>, <paramref name="language"/>): the tenant's
    /// active override if present, else the system default for that language, else the system default for "en"
    /// (BR-2 — never returns failure for a known catalog event; FR-6 fallback). Returns a 404 only for an UNKNOWN
    /// event key.
    /// </summary>
    Task<Result<ResolvedEmailTemplate>> ResolveAsync(
        string eventKey, string? language, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders the supplied subject/HTML/text against <paramref name="data"/> (FR-4/BR-5). Pure — does not touch
    /// the database. Unresolved placeholders become empty strings.
    /// </summary>
    RenderedEmail Render(
        string subject, string bodyHtml, string bodyText, IReadOnlyDictionary<string, object?> data);
}

/// <summary>
/// The effective template for an event after resolution, with provenance (US-NTF-002 FR-6).
/// <paramref name="IsCustom"/> is true when the tenant's override was used; <paramref name="Language"/> is the
/// language that actually resolved (may differ from the requested one after the "en" fallback).
/// </summary>
public sealed record ResolvedEmailTemplate(
    string EventKey,
    string Language,
    string Subject,
    string BodyHtml,
    string BodyText,
    bool IsCustom,
    int Version,
    DateTime? LastModified);

/// <summary>A rendered subject/HTML/text triple (US-NTF-002 FR-4).</summary>
public sealed record RenderedEmail(string Subject, string BodyHtml, string BodyText);
