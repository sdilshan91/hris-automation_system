using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Domain.Notifications;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-002 RESOLUTION + RENDERING seam (<see cref="IEmailTemplateService"/>). The single place that applies the
/// override→default + language-fallback rules (FR-6/BR-2) and the {{placeholder}} merge (FR-4/BR-5). Other modules
/// call this to render + send event emails so the precedence/fallback logic lives in exactly one place.
///
/// <para>Tenant isolation: the override lookup goes through the EF global query filter (current tenant); the system
/// default is read from the NON-tenant-scoped table. Resolution runs in the resolved-tenant request scope.</para>
/// </summary>
public sealed class EmailTemplateService : IEmailTemplateService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;

    public EmailTemplateService(AppDbContext db, ITenantContext tenantContext)
    {
        _db = db;
        _tenantContext = tenantContext;
    }

    public async Task<Result<ResolvedEmailTemplate>> ResolveAsync(
        string eventKey, string? language, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ResolvedEmailTemplate>.Failure("No tenant context.", 400);

        var def = NotificationEventCatalog.Get(eventKey);
        if (def is null)
            return Result<ResolvedEmailTemplate>.Failure($"Unknown notification event '{eventKey}'.", 404);

        var lang = NormalizeLanguage(language);

        // 1) Active tenant override for the requested language (AC-3/FR-6). Tenant-isolated by the global filter.
        var override_ = await _db.NotificationTemplates
            .AsNoTracking()
            .Where(t => t.EventKey == def.EventKey && t.Language == lang && t.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (override_ is not null)
        {
            return Result<ResolvedEmailTemplate>.Success(new ResolvedEmailTemplate(
                def.EventKey, override_.Language, override_.Subject, override_.BodyHtml, override_.BodyText,
                IsCustom: true, override_.Version, override_.UpdatedAt ?? override_.CreatedAt));
        }

        // 2) System default for the requested language (FR-6).
        var system = await _db.SystemNotificationTemplates
            .AsNoTracking()
            .Where(t => t.EventKey == def.EventKey && t.Language == lang)
            .FirstOrDefaultAsync(cancellationToken);

        // 3) Fall back to the "en" system default if the requested-language default is missing (BR-2/FR-6).
        if (system is null && !string.Equals(lang, NotificationEventCatalog.DefaultLanguage, StringComparison.OrdinalIgnoreCase))
        {
            system = await _db.SystemNotificationTemplates
                .AsNoTracking()
                .Where(t => t.EventKey == def.EventKey && t.Language == NotificationEventCatalog.DefaultLanguage)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (system is not null)
        {
            return Result<ResolvedEmailTemplate>.Success(new ResolvedEmailTemplate(
                def.EventKey, system.Language, system.Subject, system.BodyHtml, system.BodyText,
                IsCustom: false, Version: 0, system.UpdatedAt ?? system.CreatedAt));
        }

        // 4) Last-resort: the catalog's compiled default (BR-2 — a known event NEVER returns null). This covers an
        // unseeded DB so the renderer always has content.
        return Result<ResolvedEmailTemplate>.Success(new ResolvedEmailTemplate(
            def.EventKey, NotificationEventCatalog.DefaultLanguage,
            def.DefaultSubject, def.DefaultBodyHtml, def.DefaultBodyText,
            IsCustom: false, Version: 0, LastModified: null));
    }

    public RenderedEmail Render(
        string subject, string bodyHtml, string bodyText, IReadOnlyDictionary<string, object?> data)
        => new(
            TemplateRenderer.Render(subject, data),
            TemplateRenderer.Render(bodyHtml, data),
            TemplateRenderer.Render(bodyText, data));

    /// <summary>Normalizes a requested language to a lowercase ISO 639-1 code, defaulting to "en".</summary>
    internal static string NormalizeLanguage(string? language)
        => string.IsNullOrWhiteSpace(language)
            ? NotificationEventCatalog.DefaultLanguage
            : language.Trim().ToLowerInvariant();
}
