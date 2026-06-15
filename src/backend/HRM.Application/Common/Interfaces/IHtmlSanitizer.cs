namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Sanitizes untrusted rich-text/HTML before it is persisted, stripping scripts, event handlers,
/// and other XSS vectors (US-REC-001 NFR-4 / OWASP Top 10). Implemented in Infrastructure over a
/// vetted library rather than a hand-rolled regex (the latter is notoriously unsafe).
/// </summary>
public interface IHtmlSanitizer
{
    /// <summary>
    /// Returns a sanitized copy of <paramref name="html"/> safe to store and later render. Null/blank
    /// input returns the input unchanged.
    /// </summary>
    string? Sanitize(string? html);
}
