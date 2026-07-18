using System.Collections.Concurrent;
using FluentAssertions;
using HRM.Api.Controllers;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Common.Security;
using HRM.Application.Features.TenantSettings.DTOs;
using HRM.Application.Features.TenantSettings.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

/// <summary>
/// ISSUE-204 regression: tenant branding logos 404'd on every page because the raw internal storage path
/// (<c>/{tenantId}/branding/logo.png</c>) was exposed to the FE and no route served it. These tests prove the
/// two halves of the fix: (1) the public serve endpoint streams the stored bytes with an image content-type
/// (and 404s when nothing is uploaded), and (2) the FE-facing responses expose the SERVABLE endpoint URL — an
/// absolute URL to the serve endpoint — rather than the dead raw storage path (null when no logo exists).
/// </summary>
[Trait("TC", "TC-ADM-006-17")]
public sealed class TenantBrandingServeTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly InMemoryFileStorage _storage = new();

    public TenantBrandingServeTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.TenantId.Returns(_tenantId);
        _currentUser.IsAuthenticated.Returns(true);
    }

    // ── Serve endpoint: streams the stored bytes ──────────────────────────────

    [Fact]
    public async Task GetBrandingAsset_AfterLogoUpload_StreamsBytesWithImageContentType()
    {
        await SeedTenantAsync();
        var service = CreateService();
        var png = ValidPngBytes();

        var upload = await service.UploadBrandingAsync(BrandingAssetKind.Logo, png, "logo.png");
        upload.IsSuccess.Should().BeTrue(upload.Error);

        var result = await service.GetBrandingAssetAsync(BrandingAssetKind.Logo);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.ContentType.Should().Be("image/png");
        result.Value.Content.Should().Equal(png, "the serve path must return the exact stored bytes");
    }

    [Fact]
    public async Task GetBrandingAsset_WhenNoLogoUploaded_Returns404()
    {
        await SeedTenantAsync();
        var service = CreateService();

        var result = await service.GetBrandingAssetAsync(BrandingAssetKind.Logo);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── FE-facing exposure: servable URL, not the raw storage path ────────────

    [Fact]
    public void TenantContext_WithLogo_ExposesServableUrl_NotRawStoragePath()
    {
        // The tenant context carries the RAW stored path, exactly as the middleware populated it.
        _tenantContext.LogoUrl.Returns($"/{_tenantId}/branding/logo.png");
        var controller = BuildContextController();

        var response = OkValue<TenantContextResponse>(controller.Get());

        response.LogoUrl.Should().Be("https://acme.myhrm.org/api/v1/tenant/settings/branding/logo");
        response.LogoUrl.Should().NotContain(_tenantId.ToString(), "the raw storage path must never be exposed");
    }

    [Fact]
    public void TenantContext_WithNoLogo_ExposesNullLogoUrl()
    {
        _tenantContext.LogoUrl.Returns((string?)null);
        var controller = BuildContextController();

        var response = OkValue<TenantContextResponse>(controller.Get());

        response.LogoUrl.Should().BeNull("no logo → null so the FE shows its fallback");
    }

    [Fact]
    public async Task GetSettings_ExposesServableBrandingUrls_NotRawStoragePaths()
    {
        var branding = new BrandingDto(
            LogoUrl: $"/{_tenantId}/branding/logo.png",
            EmailLogoUrl: $"/{_tenantId}/branding/email-logo.png",
            FaviconUrl: $"/{_tenantId}/branding/favicon.ico",
            PrimaryColor: "#4F46E5");
        var dto = new TenantSettingsDto(
            new OrgProfileDto("Acme", null, null, null, null, null, 1, null),
            branding,
            new LocalizationDto("en", "dd/MM/yyyy", "1,234.56", "UTC", "USD"),
            new PasswordPolicyDto(12, true, true, true, true, 5, 90),
            new SessionPolicyDto(30, 8, 3, "deny_new"));

        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<GetTenantSettingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<TenantSettingsDto>.Success(dto));

        var controller = new TenantSettingsController(mediator)
        {
            ControllerContext = BuildControllerContext(),
        };

        var result = await controller.GetSettings(default);
        var body = OkBody<TenantSettingsDto>(result);

        body.Branding.LogoUrl.Should().Be("https://acme.myhrm.org/api/v1/tenant/settings/branding/logo");
        body.Branding.EmailLogoUrl.Should().Be("https://acme.myhrm.org/api/v1/tenant/settings/branding/email-logo");
        body.Branding.LogoUrl.Should().NotContain(_tenantId.ToString());
        body.Branding.EmailLogoUrl.Should().NotContain(_tenantId.ToString());
        body.Branding.PrimaryColor.Should().Be("#4F46E5", "non-URL branding fields are untouched");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TenantContextController BuildContextController()
        => new(_tenantContext) { ControllerContext = BuildControllerContext() };

    private static ControllerContext BuildControllerContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("acme.myhrm.org");
        return new ControllerContext { HttpContext = httpContext };
    }

    private static T OkValue<T>(IActionResult result)
    {
        var body = OkBody<T>(result);
        return body;
    }

    private static T OkBody<T>(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var api = ok.Value.Should().BeOfType<HRM.Application.DTOs.ApiResponse<T>>().Subject;
        api.Data.Should().NotBeNull();
        return api.Data!;
    }

    private TenantSettingsService CreateService()
        => new(
            TestDbContextFactory.Create(_tenantContext, _dbName),
            _tenantContext,
            _currentUser,
            _storage,
            Substitute.For<ILogger<TenantSettingsService>>(),
            cache: null);

    private async Task SeedTenantAsync()
    {
        using var db = TestDbContextFactory.Create(_tenantContext, _dbName);
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Subdomain = "acme",
            Name = "Acme",
            Status = TenantStatus.Active,
            PlanId = "default",
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static byte[] ValidPngBytes()
        => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };

    /// <summary>
    /// In-memory <see cref="IFileStorage"/> that actually PERSISTS bytes (keyed by tenant + relative path) so
    /// the upload → serve round-trip is exercised end-to-end. Mirrors LocalFileStorage's returned URL shape
    /// (<c>/{tenantId}/{relativePath}</c>), which is what the serve path must strip back down.
    /// </summary>
    private sealed class InMemoryFileStorage : IFileStorage
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new();

        public async Task<string> UploadAsync(
            Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            _files[Key(tenantId, relativePath)] = buffer.ToArray();
            return $"/{tenantId}/{relativePath}";
        }

        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult(_files.TryGetValue(Key(tenantId, relativePath), out var bytes)
                ? (Stream?)new MemoryStream(bytes)
                : null);

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
            => $"/files/{tenantId}/{relativePath}";

        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
        {
            _files.TryRemove(Key(tenantId, relativePath), out _);
            return Task.CompletedTask;
        }

        private static string Key(Guid tenantId, string relativePath) => $"{tenantId}/{relativePath}";
    }
}
