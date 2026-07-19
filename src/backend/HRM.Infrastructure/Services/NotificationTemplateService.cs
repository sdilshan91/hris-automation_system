using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.NotificationTemplates.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Notifications;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-NTF-002 tenant-admin facing template management (<see cref="INotificationTemplateService"/>). Drives the
/// list (AC-1), read (AC-2), save override (AC-3), reset (AC-4), preview (FR-4) and test-email (FR-8) flows. All
/// reads/writes run in the resolved-tenant request scope; overrides are isolated by the EF global query filter
/// (AC-5/NFR-2) and writes are audited by the existing AuditInterceptor (FR-9/NFR-6).
/// </summary>
public sealed class NotificationTemplateService : INotificationTemplateService
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IEmailTemplateService _templateService;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<NotificationTemplateService> _logger;

    public NotificationTemplateService(
        AppDbContext db,
        ITenantContext tenantContext,
        IEmailTemplateService templateService,
        IEmailSender emailSender,
        ILogger<NotificationTemplateService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _templateService = templateService;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<TemplateListItemDto>>> ListAsync(
        string? language, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<TemplateListItemDto>>.Failure("No tenant context.", 400);

        var lang = EmailTemplateService.NormalizeLanguage(language);

        // Load the tenant's active overrides for this language once (tenant-isolated by the global filter).
        var overrides = await _db.NotificationTemplates
            .AsNoTracking()
            .Where(t => t.Language == lang && t.IsActive)
            .ToDictionaryAsync(t => t.EventKey, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var items = NotificationEventCatalog.All
            .Select(def =>
            {
                var hasOverride = overrides.TryGetValue(def.EventKey, out var o);
                return new TemplateListItemDto(
                    def.EventKey,
                    def.EventName,
                    lang,
                    IsCustom: hasOverride,
                    LastModified: hasOverride ? (o!.UpdatedAt ?? o.CreatedAt) : null);
            })
            .ToList();

        return Result<IReadOnlyList<TemplateListItemDto>>.Success(items);
    }

    public async Task<Result<TemplateDetailDto>> GetAsync(
        string eventKey, string? language, CancellationToken cancellationToken = default)
    {
        var resolved = await _templateService.ResolveAsync(eventKey, language, cancellationToken);
        if (resolved.IsFailure)
            return Result<TemplateDetailDto>.Failure(resolved.Error!, resolved.StatusCode ?? 400);

        var def = NotificationEventCatalog.Get(eventKey)!; // resolution already validated the event key.
        return Result<TemplateDetailDto>.Success(ToDetail(def, resolved.Value!));
    }

    public async Task<Result<TemplateDetailDto>> SaveAsync(
        string eventKey, string? language, TemplateBodyRequest body, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<TemplateDetailDto>.Failure("No tenant context.", 400);

        var def = NotificationEventCatalog.Get(eventKey);
        if (def is null)
            return Result<TemplateDetailDto>.Failure($"Unknown notification event '{eventKey}'.", 404);

        if (body is null)
            return Result<TemplateDetailDto>.Failure("Template body is required.", 400);

        var lang = EmailTemplateService.NormalizeLanguage(language);

        var existing = await _db.NotificationTemplates
            .Where(t => t.EventKey == def.EventKey && t.Language == lang)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is null)
        {
            // BR-6/DF-5: at most N language variants per (tenant, eventKey), where N is plan-configurable
            // (was a hardcoded 2). Adding a NEW language beyond the cap is rejected (an update to an existing
            // language, or reactivating a reset one, is the `else` branch and never trips this). Soft-deleted
            // (reset) variants do not count. The cap resolves with precedence override > plan > tenant snapshot,
            // defaulting to 2 when nothing is configured so existing behaviour is preserved.
            var maxLanguageVariants = await ResolveMaxLanguageVariantsAsync(cancellationToken);
            var variantCount = await _db.NotificationTemplates
                .Where(t => t.EventKey == def.EventKey && !t.IsDeleted)
                .Select(t => t.Language)
                .Distinct()
                .CountAsync(cancellationToken);
            if (variantCount >= maxLanguageVariants)
                return Result<TemplateDetailDto>.Failure(
                    $"This template already has the maximum of {maxLanguageVariants} language variants.",
                    422, "variant_limit_reached");

            // First override for this event+language → version 1.
            var created = new NotificationTemplate
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                EventKey = def.EventKey,
                Language = lang,
                Subject = body.Subject,
                BodyHtml = body.BodyHtml,
                BodyText = body.BodyText,
                IsActive = true,
                Version = 1,
            };
            _db.NotificationTemplates.Add(created);
        }
        else
        {
            // Re-activates a previously reset (soft-deleted) override too — restore + bump version (AC-3/FR-9).
            existing.Subject = body.Subject;
            existing.BodyHtml = body.BodyHtml;
            existing.BodyText = body.BodyText;
            existing.IsActive = true;
            existing.IsDeleted = false;
            existing.Version += 1;
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Return the now-effective template (the override we just saved).
        return await GetAsync(def.EventKey, lang, cancellationToken);
    }

    /// <summary>
    /// DF-5/BR-6: resolves the effective per-(tenant, event) language-variant cap. Mirrors the
    /// <c>WorkflowService.MaxWorkflows</c> pattern — precedence override &gt; plan &gt; tenant snapshot — but
    /// defaults to the historical <c>2</c> when nothing is configured (an unresolved/unlimited value keeps
    /// today's behaviour rather than becoming truly unlimited).
    /// </summary>
    private async Task<long> ResolveMaxLanguageVariantsAsync(CancellationToken cancellationToken)
    {
        const long DefaultCap = 2;

        var tenantId = _tenantContext.TenantId;
        var tenant = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.PlanId, t.MaxTemplateLanguageVariants })
            .FirstOrDefaultAsync(cancellationToken);

        if (tenant is null)
            return DefaultCap;

        var planValue = await _db.SubscriptionPlans
            .AsNoTracking()
            .Where(p => p.Code == tenant.PlanId)
            .Select(p => (long?)p.MaxTemplateLanguageVariants)
            .FirstOrDefaultAsync(cancellationToken);
        var overrides = await _db.PlanLimitOverrides
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var resolved = PlanLimitResolver.Resolve(
            PlanLimitKeys.MaxTemplateLanguageVariants, planValue, overrides, DateTime.UtcNow);
        long? overrideValue = resolved.Source == PlanLimitResolver.LimitSource.Override
            ? resolved.Value
            : null;

        return overrideValue ?? planValue ?? tenant.MaxTemplateLanguageVariants ?? DefaultCap;
    }

    public async Task<Result<TemplateDetailDto>> ResetAsync(
        string eventKey, string? language, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<TemplateDetailDto>.Failure("No tenant context.", 400);

        var def = NotificationEventCatalog.Get(eventKey);
        if (def is null)
            return Result<TemplateDetailDto>.Failure($"Unknown notification event '{eventKey}'.", 404);

        var lang = EmailTemplateService.NormalizeLanguage(language);

        var existing = await _db.NotificationTemplates
            .Where(t => t.EventKey == def.EventKey && t.Language == lang && t.IsActive)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            // Soft-delete the override → revert to the system default (AC-4). The AuditInterceptor records the change.
            existing.IsActive = false;
            existing.IsDeleted = true;
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Return the now-effective default.
        return await GetAsync(def.EventKey, lang, cancellationToken);
    }

    public async Task<Result<RenderedPreviewDto>> PreviewAsync(
        string eventKey, PreviewTemplateRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<RenderedPreviewDto>.Failure("No tenant context.", 400);

        var def = NotificationEventCatalog.Get(eventKey);
        if (def is null)
            return Result<RenderedPreviewDto>.Failure($"Unknown notification event '{eventKey}'.", 404);

        if (request is null)
            return Result<RenderedPreviewDto>.Failure("Preview request is required.", 400);

        var rendered = _templateService.Render(
            request.Subject ?? string.Empty,
            request.BodyHtml ?? string.Empty,
            request.BodyText ?? string.Empty,
            def.SampleData);

        return Result<RenderedPreviewDto>.Success(
            new RenderedPreviewDto(rendered.Subject, rendered.BodyHtml, rendered.BodyText));
    }

    public async Task<Result<TestEmailResultDto>> SendTestEmailAsync(
        string eventKey, TestEmailRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<TestEmailResultDto>.Failure("No tenant context.", 400);

        if (request is null || string.IsNullOrWhiteSpace(request.RecipientEmail))
            return Result<TestEmailResultDto>.Failure("Recipient email is required.", 400);

        // Resolve the EFFECTIVE template (override if any, else default) and render it with the catalog sample data.
        var resolved = await _templateService.ResolveAsync(eventKey, request.Language, cancellationToken);
        if (resolved.IsFailure)
            return Result<TestEmailResultDto>.Failure(resolved.Error!, resolved.StatusCode ?? 400);

        var def = NotificationEventCatalog.Get(eventKey)!;
        var rendered = _templateService.Render(
            resolved.Value!.Subject, resolved.Value.BodyHtml, resolved.Value.BodyText, def.SampleData);

        await _emailSender.SendAsync(
            new EmailMessage(
                _tenantContext.TenantId,
                request.RecipientEmail.Trim(),
                rendered.Subject,
                rendered.BodyHtml,
                rendered.BodyText),
            cancellationToken);

        _logger.LogInformation(
            "Dispatched test email for event '{EventKey}' to {Recipient} (tenant {TenantId}).",
            def.EventKey, request.RecipientEmail, _tenantContext.TenantId);

        return Result<TestEmailResultDto>.Success(new TestEmailResultDto(Sent: true));
    }

    private static TemplateDetailDto ToDetail(NotificationEventDefinition def, ResolvedEmailTemplate resolved)
        => new(
            def.EventKey,
            def.EventName,
            resolved.Language,
            resolved.Subject,
            resolved.BodyHtml,
            resolved.BodyText,
            resolved.IsCustom,
            resolved.Version,
            def.Placeholders,
            def.SampleData);
}
