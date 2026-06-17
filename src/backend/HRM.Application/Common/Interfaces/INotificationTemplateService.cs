using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationTemplates.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Tenant-admin facing template management (US-NTF-002): list events with override status (AC-1), read the
/// effective template + placeholders/sample-data (AC-2), upsert an override (AC-3), reset to default (AC-4),
/// live preview (FR-4) and a test-email send (FR-8). Every operation runs in the resolved-tenant request scope;
/// overrides are isolated by the EF global query filter (AC-5/NFR-2).
/// </summary>
public interface INotificationTemplateService
{
    /// <summary>AC-1: one row per catalog event for <paramref name="language"/>, with override status + last-modified.</summary>
    Task<Result<IReadOnlyList<TemplateListItemDto>>> ListAsync(
        string? language, CancellationToken cancellationToken = default);

    /// <summary>AC-2: the effective template for an event + language, with placeholders + sample data. 404 if unknown event.</summary>
    Task<Result<TemplateDetailDto>> GetAsync(
        string eventKey, string? language, CancellationToken cancellationToken = default);

    /// <summary>AC-3: upsert the tenant override for an event + language; increments version. 404 if unknown event.</summary>
    Task<Result<TemplateDetailDto>> SaveAsync(
        string eventKey, string? language, TemplateBodyRequest body, CancellationToken cancellationToken = default);

    /// <summary>AC-4: remove (soft-delete) the tenant override for an event + language → reverts to the default.</summary>
    Task<Result<TemplateDetailDto>> ResetAsync(
        string eventKey, string? language, CancellationToken cancellationToken = default);

    /// <summary>FR-4: render the supplied bodies against the catalog's sample data. Does not persist. 404 if unknown event.</summary>
    Task<Result<RenderedPreviewDto>> PreviewAsync(
        string eventKey, PreviewTemplateRequest request, CancellationToken cancellationToken = default);

    /// <summary>FR-8: render the effective template against sample data and send it to <paramref name="recipientEmail"/>.</summary>
    Task<Result<TestEmailResultDto>> SendTestEmailAsync(
        string eventKey, TestEmailRequest request, CancellationToken cancellationToken = default);
}
