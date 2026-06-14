using HRM.Application.Common.Interfaces;

namespace HRM.Infrastructure.Services;

/// <summary>
/// <see cref="IHtmlSanitizer"/> implementation over the vetted Ganss.Xss HtmlSanitizer (allow-list
/// based). Strips &lt;script&gt;, event-handler attributes (onload/onclick/…), javascript: URLs and
/// other XSS vectors while preserving the safe formatting a rich-text editor produces (US-REC-001
/// NFR-4 / OWASP). Registered as a singleton — the underlying sanitizer is thread-safe for Sanitize().
/// </summary>
public sealed class GanssHtmlSanitizer : IHtmlSanitizer
{
    private readonly Ganss.Xss.HtmlSanitizer _sanitizer;

    public GanssHtmlSanitizer()
    {
        // Start from the library's safe defaults (a curated allow-list of tags/attributes/schemes) and
        // tighten further: disallow inline styles entirely to avoid CSS-based exfiltration/clickjacking.
        _sanitizer = new Ganss.Xss.HtmlSanitizer();
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");
        // Drop style attributes/tags (defence-in-depth; rich-text bodies rarely need raw CSS here).
        _sanitizer.AllowedAttributes.Remove("style");
        _sanitizer.AllowedTags.Remove("style");
        _sanitizer.AllowedCssProperties.Clear();
    }

    public string? Sanitize(string? html)
        => string.IsNullOrWhiteSpace(html) ? html : _sanitizer.Sanitize(html);
}
