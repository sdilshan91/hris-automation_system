// ============================================================================
// BUG-005 regression (US-ADM-006 AC-3 / FR-4 — localization validation).
//
// TenantSettingsService.UpdateLocalizationAsync must REJECT an invalid time zone,
// currency, or date format with 400 (a valid combination still succeeds). Pre-fix
// the service only validated DefaultLanguage (SupportedLanguages) and stored the
// TimeZone / Currency / DateFormat verbatim, so bogus values were accepted — these
// reject tests FAIL pre-fix (the call returns IsSuccess and the row is mutated) and
// PASS once the parallel fix adds the field validation.
//
// InMemory is appropriate: this is app-level input validation on a single tenant row.
// Traceability: @BUG-005.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.TenantSettings.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class TenantSettingsLocalizationValidationTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    // Known-good localization the tenant is seeded with; a rejected update must leave these untouched.
    private const string SeededTimeZone = "UTC";
    private const string SeededCurrency = "USD";
    private const string SeededDateFormat = "dd/MM/yyyy";

    public TenantSettingsLocalizationValidationTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _currentUser.IsAuthenticated.Returns(true);
    }

    // ── Reject: invalid time zone ────────────────────────────────────────────

    [Fact]
    public async Task UpdateLocalization_InvalidTimeZone_IsRejected_BUG005()
    {
        await SeedTenantAsync();
        var service = CreateService();

        // Valid language + currency + date format, but a time zone id that does not exist.
        var result = await service.UpdateLocalizationAsync(new UpdateLocalizationRequest(
            DefaultLanguage: "en",
            DateFormat: SeededDateFormat,
            NumberFormat: "1,234.56",
            TimeZone: "Mars/Phobos",
            Currency: SeededCurrency));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        await AssertLocalizationUnchangedAsync();
    }

    // ── Reject: invalid currency ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateLocalization_InvalidCurrency_IsRejected_BUG005()
    {
        await SeedTenantAsync();
        var service = CreateService();

        // "XX" is neither a 3-letter code nor a real ISO-4217 currency.
        var result = await service.UpdateLocalizationAsync(new UpdateLocalizationRequest(
            DefaultLanguage: "en",
            DateFormat: SeededDateFormat,
            NumberFormat: "1,234.56",
            TimeZone: SeededTimeZone,
            Currency: "XX"));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        await AssertLocalizationUnchangedAsync();
    }

    // ── Reject: invalid date format ──────────────────────────────────────────

    [Fact]
    public async Task UpdateLocalization_InvalidDateFormat_IsRejected_BUG005()
    {
        await SeedTenantAsync();
        var service = CreateService();

        var result = await service.UpdateLocalizationAsync(new UpdateLocalizationRequest(
            DefaultLanguage: "en",
            DateFormat: "not-a-date-format",
            NumberFormat: "1,234.56",
            TimeZone: SeededTimeZone,
            Currency: SeededCurrency));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        await AssertLocalizationUnchangedAsync();
    }

    // ── Control: a fully valid combination still succeeds ────────────────────

    [Fact]
    public async Task UpdateLocalization_ValidCombo_Succeeds_BUG005()
    {
        await SeedTenantAsync();
        var service = CreateService();

        var result = await service.UpdateLocalizationAsync(new UpdateLocalizationRequest(
            DefaultLanguage: "en",
            DateFormat: "dd/MM/yyyy",
            NumberFormat: "1,234.56",
            TimeZone: "UTC",
            Currency: "USD"));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.TimeZone.Should().Be("UTC");
        result.Value.Currency.Should().Be("USD");

        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        tenant.TimeZone.Should().Be("UTC");
        tenant.Currency.Should().Be("USD");
        tenant.DateFormat.Should().Be("dd/MM/yyyy");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private TenantSettingsService CreateService()
        => new(CreateDbContext(), _tenantContext, _currentUser,
            Substitute.For<IFileStorage>(), Substitute.For<ILogger<TenantSettingsService>>(), cache: null);

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private async Task SeedTenantAsync()
    {
        using var db = CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Subdomain = "acme",
            Name = "Acme",
            Status = TenantStatus.Active,
            PlanId = "default",
            DefaultLanguage = "en",
            DateFormat = SeededDateFormat,
            NumberFormat = "1,234.56",
            TimeZone = SeededTimeZone,
            Currency = SeededCurrency,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A rejected update must not mutate the persisted localization columns.</summary>
    private async Task AssertLocalizationUnchangedAsync()
    {
        using var db = CreateDbContext();
        var tenant = await db.Tenants.IgnoreQueryFilters().SingleAsync(t => t.Id == _tenantId);
        tenant.TimeZone.Should().Be(SeededTimeZone);
        tenant.Currency.Should().Be(SeededCurrency);
        tenant.DateFormat.Should().Be(SeededDateFormat);
    }
}
