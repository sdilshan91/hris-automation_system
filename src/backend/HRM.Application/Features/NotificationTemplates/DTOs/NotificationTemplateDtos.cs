namespace HRM.Application.Features.NotificationTemplates.DTOs;

/// <summary>
/// A row in the template list (US-NTF-002 AC-1). One entry per catalog event for the requested language.
/// <c>IsCustom</c> shows whether the tenant has an active override (vs. the system default); <c>LastModified</c>
/// is the override's last-update time, or null when the system default is in effect.
/// </summary>
public sealed record TemplateListItemDto(
    string EventKey,
    string EventName,
    string Language,
    bool IsCustom,
    DateTime? LastModified);

/// <summary>
/// The full effective template for an event + language (US-NTF-002 AC-2). Carries the editable bodies, the
/// provenance (<c>IsCustom</c>/<c>Version</c>), the catalog's available placeholders (FR-3 reference panel) and
/// the sample data (FR-4 live preview).
/// </summary>
public sealed record TemplateDetailDto(
    string EventKey,
    string EventName,
    string Language,
    string Subject,
    string BodyHtml,
    string BodyText,
    bool IsCustom,
    int Version,
    IReadOnlyList<string> AvailablePlaceholders,
    IReadOnlyDictionary<string, object?> SampleData);

/// <summary>A rendered subject/HTML/text triple returned by preview + the resolved-template render (AC-2/FR-4).</summary>
public sealed record RenderedPreviewDto(
    string Subject,
    string BodyHtml,
    string BodyText);

/// <summary>Result of a test-email send (US-NTF-002 FR-8/AC-2 — matches the pinned FE contract { sent }).</summary>
public sealed record TestEmailResultDto(bool Sent);

/// <summary>Request body for save/preview (US-NTF-002 FR-1).</summary>
public sealed record TemplateBodyRequest(string Subject, string BodyHtml, string BodyText);

/// <summary>Request body for preview, which also carries the language (US-NTF-002 FR-4 — pinned FE contract).</summary>
public sealed record PreviewTemplateRequest(string Subject, string BodyHtml, string BodyText, string? Language);

/// <summary>Request body for the test-email send (US-NTF-002 FR-8 — pinned FE contract).</summary>
public sealed record TestEmailRequest(string RecipientEmail, string? Language);
