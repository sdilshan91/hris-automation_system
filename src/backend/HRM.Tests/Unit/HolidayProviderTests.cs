// ============================================================================
// US-LV-007 AC-2 / FR-6: DB-backed HolidayProvider unit tests.
//
// Proves the provider returns active PUBLIC holidays in a date range for the
// tenant, honours location filtering (BR-2), and — the key integration with the
// leave-day calculation — that WorkingDaysCalculator now EXCLUDES a public
// holiday fed by the provider (Mon-Fri with a Wednesday holiday = 4 days).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveRequests;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class HolidayProviderTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly Guid _nyLocationId = Guid.NewGuid();

    public HolidayProviderTests()
    {
        _tenantContext = TestTenantContext(_tenantId);
    }

    private static ITenantContext TestTenantContext(Guid tenantId)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);
        return ctx;
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private HolidayProvider CreateProvider() => new(CreateDbContext());

    private void AddHoliday(
        DateOnly date, HolidayType type = HolidayType.Public,
        Guid? locationId = null, bool isActive = true)
    {
        using var db = CreateDbContext();
        db.Holidays.Add(new Holiday
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            Name = "Holiday",
            Date = date,
            Type = type,
            LocationId = locationId,
            IsActive = isActive,
            IsRecurring = false,
            IsDeleted = false,
        });
        db.SaveChanges();
    }

    // ── Range + type filtering ─────────────────────────────────────

    [Fact]
    public async Task GetHolidays_ReturnsPublicHolidaysInRange()
    {
        AddHoliday(new DateOnly(2026, 1, 1));
        AddHoliday(new DateOnly(2026, 6, 15));
        AddHoliday(new DateOnly(2026, 12, 25));

        var result = await CreateProvider().GetHolidaysAsync(
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        result.Should().ContainSingle().Which.Should().Be(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public async Task GetHolidays_ExcludesRestrictedAndOptional()
    {
        AddHoliday(new DateOnly(2026, 6, 10), HolidayType.Public);
        AddHoliday(new DateOnly(2026, 6, 11), HolidayType.Restricted);
        AddHoliday(new DateOnly(2026, 6, 12), HolidayType.Optional);

        var result = await CreateProvider().GetHolidaysAsync(
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        result.Should().ContainSingle().Which.Should().Be(new DateOnly(2026, 6, 10));
    }

    [Fact]
    public async Task GetHolidays_ExcludesDeactivated()
    {
        AddHoliday(new DateOnly(2026, 6, 10), isActive: false);

        var result = await CreateProvider().GetHolidaysAsync(
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHolidays_InvertedRange_ReturnsEmpty()
    {
        AddHoliday(new DateOnly(2026, 6, 10));

        var result = await CreateProvider().GetHolidaysAsync(
            new DateOnly(2026, 6, 30), new DateOnly(2026, 6, 1));

        result.Should().BeEmpty();
    }

    // ── Location filtering (BR-2) ──────────────────────────────────

    [Fact]
    public async Task GetHolidays_NoLocation_ReturnsOnlyTenantWide()
    {
        AddHoliday(new DateOnly(2026, 6, 10));                       // tenant-wide
        AddHoliday(new DateOnly(2026, 6, 11), locationId: _nyLocationId); // NY-only

        // No location supplied -> only tenant-wide holidays count.
        var result = await CreateProvider().GetHolidaysAsync(
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30));

        result.Should().ContainSingle().Which.Should().Be(new DateOnly(2026, 6, 10));
    }

    [Fact]
    public async Task GetHolidays_WithLocation_ReturnsTenantWidePlusThatLocation()
    {
        AddHoliday(new DateOnly(2026, 6, 10));                       // tenant-wide
        AddHoliday(new DateOnly(2026, 6, 11), locationId: _nyLocationId); // NY-only
        AddHoliday(new DateOnly(2026, 6, 12), locationId: Guid.NewGuid()); // other location

        var result = await CreateProvider().GetHolidaysAsync(
            new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), _nyLocationId);

        result.Should().BeEquivalentTo(new[]
        {
            new DateOnly(2026, 6, 10),
            new DateOnly(2026, 6, 11),
        });
        result.Should().NotContain(new DateOnly(2026, 6, 12)); // London-style: other location excluded
    }

    // ── AC-2: the key leave-calc integration ───────────────────────

    [Fact]
    public async Task WorkingDays_MonToFri_WithWednesdayHoliday_Is4Days()
    {
        // A concrete Mon-Fri week: 2026-06-15 (Mon) .. 2026-06-19 (Fri).
        var monday = new DateOnly(2026, 6, 15);
        var friday = new DateOnly(2026, 6, 19);
        var wednesday = new DateOnly(2026, 6, 17);
        monday.DayOfWeek.Should().Be(DayOfWeek.Monday);
        wednesday.DayOfWeek.Should().Be(DayOfWeek.Wednesday);

        AddHoliday(wednesday); // public holiday on the Wednesday

        var holidays = await CreateProvider().GetHolidaysAsync(monday, friday);
        var days = WorkingDaysCalculator.CountWorkingDays(monday, friday, holidays: holidays);

        // 5 weekdays minus the 1 public holiday = 4 leave days (AC-2 / Test Hint).
        days.Should().Be(4m);
    }

    [Fact]
    public async Task WorkingDays_MonToFri_NoHoliday_Is5Days()
    {
        var monday = new DateOnly(2026, 6, 15);
        var friday = new DateOnly(2026, 6, 19);

        var holidays = await CreateProvider().GetHolidaysAsync(monday, friday);
        var days = WorkingDaysCalculator.CountWorkingDays(monday, friday, holidays: holidays);

        days.Should().Be(5m);
    }
}
