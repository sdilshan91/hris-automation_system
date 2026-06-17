// ============================================================================
// US-NTF-002: Email Notification Templates per Tenant — unit tests.
//
//   - TemplateRenderer (pure): resolves nested {{path}} placeholders; unresolved/null/non-dict paths → "" (BR-5).
//   - EmailTemplateService.Resolve: tenant override wins over system default (AC-3); falls back to the system
//     default when no override (FR-6); falls back to the "en" default when the requested language is missing
//     (BR-2); returns 404 for an unknown event; never returns null content for a known event.
//   - NotificationTemplateService.Save: first save = version 1; re-save increments version (FR-9).
//   - NotificationTemplateService.Reset: soft-deletes the override → resolution reverts to the system default (AC-4).
//   - Tenant isolation: tenant A's override is invisible to tenant B; B still sees the system default (AC-5).
//   - List: marks the event custom only for the tenant that has the override.
//   - SendTestEmail: renders the effective template and dispatches via the (recording) email sender (FR-8).
//
// PROVIDER: EF InMemory through the real services + a recording IEmailSender (the verify gate runs `dotnet test`
// with no PostgreSQL / Docker / SMTP). Seeds the system-default templates the same way DbInitializer does.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.NotificationTemplates.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Notifications;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Unit;

public sealed class NotificationTemplateTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private const string LeaveApproved = "leave_approved";

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection().Build();

    // ── Builders ─────────────────────────────────────────────────────────────

    /// <summary>A DbContext scoped to <paramref name="tenantId"/> (Guid.Empty = no tenant, e.g. seeding).</summary>
    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private EmailTemplateService TemplateSvc(Guid tenantId)
        => new(Db(tenantId), new MutableTenantContext { TenantId = tenantId });

    private NotificationTemplateService MgmtSvc(Guid tenantId, IEmailSender? sender = null)
    {
        var tc = new MutableTenantContext { TenantId = tenantId };
        return new NotificationTemplateService(
            Db(tenantId), tc, TemplateSvc(tenantId), sender ?? new RecordingEmailSender(),
            NullLogger<NotificationTemplateService>.Instance);
    }

    /// <summary>Seeds the system-default templates exactly as DbInitializer does (en, one per catalog event).</summary>
    private async Task SeedSystemDefaultsAsync()
    {
        using var db = Db(Guid.Empty);
        foreach (var def in NotificationEventCatalog.All)
        {
            db.SystemNotificationTemplates.Add(new SystemNotificationTemplate
            {
                Id = BaseEntity.NewUuidV7(),
                EventKey = def.EventKey,
                Language = NotificationEventCatalog.DefaultLanguage,
                Subject = def.DefaultSubject,
                BodyHtml = def.DefaultBodyHtml,
                BodyText = def.DefaultBodyText,
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }

    // ── TemplateRenderer (pure) ────────────────────────────────────────────────

    [Fact]
    public void Renderer_ResolvesNestedPlaceholders()
    {
        var data = new Dictionary<string, object?>
        {
            ["employee"] = new Dictionary<string, object?> { ["firstName"] = "Jane" },
            ["leave"] = new Dictionary<string, object?> { ["days"] = 5 },
        };

        var result = TemplateRenderer.Render("Hi {{employee.firstName}}, {{leave.days}} day(s).", data);

        result.Should().Be("Hi Jane, 5 day(s).");
    }

    [Fact]
    public void Renderer_UnresolvedPlaceholder_BecomesEmptyString_NotRawText()
    {
        var data = new Dictionary<string, object?>
        {
            ["employee"] = new Dictionary<string, object?> { ["firstName"] = "Jane" },
        };

        // Unknown leaf, walking into a non-dictionary, and a null value all collapse to "" (BR-5).
        var result = TemplateRenderer.Render(
            "[{{employee.lastName}}][{{employee.firstName.x}}][{{missing.path}}]", data);

        result.Should().Be("[][][]");
        result.Should().NotContain("{{");
    }

    [Fact]
    public void Renderer_NullValue_BecomesEmptyString()
    {
        var data = new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?> { ["name"] = null },
        };

        TemplateRenderer.Render("Name=[{{user.name}}]", data).Should().Be("Name=[]");
    }

    // ── Resolution / fallback ──────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_UnknownEvent_Returns404()
    {
        await SeedSystemDefaultsAsync();

        var result = await TemplateSvc(_tenantA).ResolveAsync("not_a_real_event", "en");

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Resolve_NoOverride_FallsBackToSystemDefault()
    {
        await SeedSystemDefaultsAsync();

        var result = await TemplateSvc(_tenantA).ResolveAsync(LeaveApproved, "en");

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsCustom.Should().BeFalse();
        result.Value.Subject.Should().Be(NotificationEventCatalog.Get(LeaveApproved)!.DefaultSubject);
    }

    [Fact]
    public async Task Resolve_ActiveOverride_TakesPrecedenceOverDefault()
    {
        await SeedSystemDefaultsAsync();
        await MgmtSvc(_tenantA).SaveAsync(LeaveApproved, "en",
            new TemplateBodyRequest("Custom subject", "<p>custom html</p>", "custom text"));

        var result = await TemplateSvc(_tenantA).ResolveAsync(LeaveApproved, "en");

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsCustom.Should().BeTrue();
        result.Value.Subject.Should().Be("Custom subject");
    }

    [Fact]
    public async Task Resolve_MissingLanguageVariant_FallsBackToEnglishDefault()
    {
        await SeedSystemDefaultsAsync(); // only "en" defaults seeded.

        // Request "fr" — no fr override and no fr system default → resolves the "en" system default (BR-2/FR-6).
        var result = await TemplateSvc(_tenantA).ResolveAsync(LeaveApproved, "fr");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Language.Should().Be("en");
        result.Value.Subject.Should().Be(NotificationEventCatalog.Get(LeaveApproved)!.DefaultSubject);
    }

    [Fact]
    public async Task Resolve_KnownEvent_WithNoSeededDefaults_StillReturnsCatalogDefault()
    {
        // No DB seeding at all — a known catalog event must never return null content (BR-2).
        var result = await TemplateSvc(_tenantA).ResolveAsync(LeaveApproved, "en");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Subject.Should().Be(NotificationEventCatalog.Get(LeaveApproved)!.DefaultSubject);
    }

    // ── Save (versioning) ──────────────────────────────────────────────────────

    [Fact]
    public async Task Save_FirstOverride_IsVersionOne()
    {
        await SeedSystemDefaultsAsync();

        var saved = await MgmtSvc(_tenantA).SaveAsync(LeaveApproved, "en",
            new TemplateBodyRequest("v1", "<p>v1</p>", "v1"));

        saved.IsSuccess.Should().BeTrue();
        saved.Value!.IsCustom.Should().BeTrue();
        saved.Value.Version.Should().Be(1);
    }

    [Fact]
    public async Task Save_Resave_IncrementsVersion()
    {
        await SeedSystemDefaultsAsync();
        await MgmtSvc(_tenantA).SaveAsync(LeaveApproved, "en", new TemplateBodyRequest("v1", "<p>v1</p>", "v1"));

        var second = await MgmtSvc(_tenantA).SaveAsync(LeaveApproved, "en",
            new TemplateBodyRequest("v2", "<p>v2</p>", "v2"));

        second.IsSuccess.Should().BeTrue();
        second.Value!.Version.Should().Be(2);
        second.Value.Subject.Should().Be("v2");
    }

    [Fact]
    public async Task Save_UnknownEvent_Returns404()
    {
        await SeedSystemDefaultsAsync();

        var result = await MgmtSvc(_tenantA).SaveAsync("nope", "en",
            new TemplateBodyRequest("s", "<p>h</p>", "t"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Reset (revert to default) ──────────────────────────────────────────────

    [Fact]
    public async Task Reset_RemovesOverride_RevertsToSystemDefault()
    {
        await SeedSystemDefaultsAsync();
        await MgmtSvc(_tenantA).SaveAsync(LeaveApproved, "en", new TemplateBodyRequest("custom", "<p>c</p>", "c"));

        var reset = await MgmtSvc(_tenantA).ResetAsync(LeaveApproved, "en");

        reset.IsSuccess.Should().BeTrue();
        reset.Value!.IsCustom.Should().BeFalse();
        reset.Value.Subject.Should().Be(NotificationEventCatalog.Get(LeaveApproved)!.DefaultSubject);

        // Resolution after reset also returns the default.
        var resolved = await TemplateSvc(_tenantA).ResolveAsync(LeaveApproved, "en");
        resolved.Value!.IsCustom.Should().BeFalse();
    }

    [Fact]
    public async Task Reset_ThenSave_CreatesFreshOverride_NoUniqueCollision()
    {
        await SeedSystemDefaultsAsync();
        await MgmtSvc(_tenantA).SaveAsync(LeaveApproved, "en", new TemplateBodyRequest("first", "<p>1</p>", "1"));
        await MgmtSvc(_tenantA).ResetAsync(LeaveApproved, "en");

        var resaved = await MgmtSvc(_tenantA).SaveAsync(LeaveApproved, "en",
            new TemplateBodyRequest("again", "<p>2</p>", "2"));

        resaved.IsSuccess.Should().BeTrue();
        resaved.Value!.IsCustom.Should().BeTrue();
        resaved.Value.Subject.Should().Be("again");
    }

    // ── Tenant isolation (AC-5) ────────────────────────────────────────────────

    [Fact]
    public async Task TenantOverride_IsInvisibleToOtherTenant()
    {
        await SeedSystemDefaultsAsync();

        // Tenant A customizes leave_approved.
        await MgmtSvc(_tenantA).SaveAsync(LeaveApproved, "en",
            new TemplateBodyRequest("A custom", "<p>A</p>", "A"));

        // Tenant A sees its override; tenant B still sees the system default.
        var aResolved = await TemplateSvc(_tenantA).ResolveAsync(LeaveApproved, "en");
        var bResolved = await TemplateSvc(_tenantB).ResolveAsync(LeaveApproved, "en");

        aResolved.Value!.IsCustom.Should().BeTrue();
        aResolved.Value.Subject.Should().Be("A custom");

        bResolved.Value!.IsCustom.Should().BeFalse();
        bResolved.Value.Subject.Should().Be(NotificationEventCatalog.Get(LeaveApproved)!.DefaultSubject);
    }

    [Fact]
    public async Task List_MarksCustom_OnlyForTenantWithOverride()
    {
        await SeedSystemDefaultsAsync();
        await MgmtSvc(_tenantA).SaveAsync(LeaveApproved, "en",
            new TemplateBodyRequest("A custom", "<p>A</p>", "A"));

        var aList = await MgmtSvc(_tenantA).ListAsync("en");
        var bList = await MgmtSvc(_tenantB).ListAsync("en");

        aList.Value!.Single(i => i.EventKey == LeaveApproved).IsCustom.Should().BeTrue();
        bList.Value!.Single(i => i.EventKey == LeaveApproved).IsCustom.Should().BeFalse();

        // Every catalog event appears in the list (AC-1).
        aList.Value.Should().HaveCount(NotificationEventCatalog.All.Count);
    }

    // ── Preview + test email ───────────────────────────────────────────────────

    [Fact]
    public async Task Preview_RendersSuppliedBody_WithSampleData()
    {
        await SeedSystemDefaultsAsync();

        var preview = await MgmtSvc(_tenantA).PreviewAsync(LeaveApproved,
            new PreviewTemplateRequest("Hi {{employee.firstName}}", "<p>{{leave.type}}</p>", "{{leave.type}}", "en"));

        preview.IsSuccess.Should().BeTrue();
        preview.Value!.Subject.Should().Be("Hi Jane");          // catalog sample data: employee.firstName = "Jane"
        preview.Value.BodyHtml.Should().Be("<p>Annual Leave</p>"); // leave.type = "Annual Leave"
    }

    [Fact]
    public async Task SendTestEmail_RendersEffectiveTemplate_AndDispatches()
    {
        await SeedSystemDefaultsAsync();
        var sender = new RecordingEmailSender();

        var result = await MgmtSvc(_tenantA, sender).SendTestEmailAsync(LeaveApproved,
            new TestEmailRequest("admin@acme.test", "en"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Sent.Should().BeTrue();

        sender.Sent.Should().ContainSingle();
        var msg = sender.Sent[0];
        msg.RecipientEmail.Should().Be("admin@acme.test");
        msg.TenantId.Should().Be(_tenantA);
        // Sample data merged: no unresolved placeholders survive (BR-5).
        msg.BodyHtml.Should().NotContain("{{");
        msg.Subject.Should().NotContain("{{");
    }

    [Fact]
    public async Task SendTestEmail_MissingRecipient_Fails()
    {
        await SeedSystemDefaultsAsync();

        var result = await MgmtSvc(_tenantA).SendTestEmailAsync(LeaveApproved, new TestEmailRequest("", "en"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }
}
