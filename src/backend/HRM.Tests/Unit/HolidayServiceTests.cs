// ============================================================================
// US-LV-007: Holiday calendar service unit tests.
// Covers CRUD, BR-1 duplicate-date prevention (tenant + location), CSV import
// (valid rows created, duplicates flagged), soft-deactivate (BR-4), and the
// recurring-holiday next-year generation (FR-3, BR-5) idempotency.
// Uses EF Core InMemory provider (mirrors LeaveRequestServiceTests).
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Holidays.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class HolidayServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<HolidayService> _logger;

    private readonly Guid _locationId = Guid.NewGuid();

    public HolidayServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");

        _logger = Substitute.For<ILogger<HolidayService>>();

        SeedLocation();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private HolidayService CreateService()
        => new(CreateDbContext(), _tenantContext, _currentUser, _logger);

    private void SeedLocation()
    {
        using var db = CreateDbContext();
        db.Locations.Add(new Location
        {
            Id = _locationId,
            TenantId = _tenantId,
            Name = "New York",
            TimeZone = "America/New_York",
            IsActive = true,
        });
        db.SaveChanges();
    }

    private static CreateHolidayRequest Req(
        DateOnly date, string name = "Holiday", string type = "Public",
        Guid? locationId = null, bool isRecurring = false) => new()
        {
            Name = name,
            Date = date,
            Type = type,
            LocationId = locationId,
            IsRecurring = isRecurring,
        };

    // ── Create (FR-1, AC-1) ────────────────────────────────────────

    [Fact]
    public async Task Create_ValidHoliday_Succeeds()
    {
        var svc = CreateService();
        var result = await svc.CreateAsync(Req(new DateOnly(2026, 1, 1), "New Year's Day"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("New Year's Day");
        result.Value.Type.Should().Be("Public");
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Create_TrimsNameAndDescription()
    {
        var svc = CreateService();
        var result = await svc.CreateAsync(new CreateHolidayRequest
        {
            Name = "  Labour Day  ",
            Date = new DateOnly(2026, 5, 1),
            Type = "Public",
            Description = "  rest  ",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Name.Should().Be("Labour Day");
        result.Value.Description.Should().Be("rest");
    }

    [Fact]
    public async Task Create_InvalidType_Fails()
    {
        var svc = CreateService();
        var result = await svc.CreateAsync(Req(new DateOnly(2026, 1, 1), type: "Bogus"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Invalid holiday type");
    }

    [Fact]
    public async Task Create_UnknownLocation_Fails()
    {
        var svc = CreateService();
        var result = await svc.CreateAsync(Req(new DateOnly(2026, 1, 1), locationId: Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Location not found");
    }

    [Fact]
    public async Task Create_WithKnownLocation_Succeeds()
    {
        var svc = CreateService();
        var result = await svc.CreateAsync(Req(new DateOnly(2026, 1, 1), locationId: _locationId));

        result.IsSuccess.Should().BeTrue();
        result.Value!.LocationId.Should().Be(_locationId);
        result.Value.LocationName.Should().Be("New York");
    }

    // ── BR-1: duplicate-date prevention ────────────────────────────

    [Fact]
    public async Task Create_DuplicateTenantWideDate_Fails()
    {
        var date = new DateOnly(2026, 1, 1);
        (await CreateService().CreateAsync(Req(date, "First"))).IsSuccess.Should().BeTrue();

        var dup = await CreateService().CreateAsync(Req(date, "Second"));

        dup.IsFailure.Should().BeTrue();
        dup.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task Create_SameDateDifferentLocation_Succeeds()
    {
        var date = new DateOnly(2026, 1, 1);
        // Tenant-wide on the same date.
        (await CreateService().CreateAsync(Req(date, "TenantWide"))).IsSuccess.Should().BeTrue();
        // Location-scoped on the same date is a different (date, location) combo -> allowed (BR-1).
        var locScoped = await CreateService().CreateAsync(Req(date, "NY-only", locationId: _locationId));

        locScoped.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_DuplicateLocationScopedDate_Fails()
    {
        var date = new DateOnly(2026, 1, 1);
        (await CreateService().CreateAsync(Req(date, "First", locationId: _locationId)))
            .IsSuccess.Should().BeTrue();

        var dup = await CreateService().CreateAsync(Req(date, "Second", locationId: _locationId));

        dup.IsFailure.Should().BeTrue();
        dup.Error.Should().Contain("for this location");
    }

    // ── Update (FR-1) ──────────────────────────────────────────────

    [Fact]
    public async Task Update_ChangesFields()
    {
        var create = await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1), "Old"));
        var id = create.Value!.Id;

        var update = await CreateService().UpdateAsync(id, new UpdateHolidayRequest
        {
            Name = "New",
            Date = new DateOnly(2026, 1, 2),
            Type = "Restricted",
            IsRecurring = true,
        });

        update.IsSuccess.Should().BeTrue();
        update.Value!.Name.Should().Be("New");
        update.Value.Date.Should().Be(new DateOnly(2026, 1, 2));
        update.Value.Type.Should().Be("Restricted");
        update.Value.IsRecurring.Should().BeTrue();
    }

    [Fact]
    public async Task Update_ToAnotherExistingDate_Fails()
    {
        (await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1), "Jan1"))).IsSuccess.Should().BeTrue();
        var second = await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 2), "Jan2"));

        // Move Jan2 onto Jan1 -> duplicate.
        var move = await CreateService().UpdateAsync(second.Value!.Id, new UpdateHolidayRequest
        {
            Name = "Jan2",
            Date = new DateOnly(2026, 1, 1),
            Type = "Public",
        });

        move.IsFailure.Should().BeTrue();
        move.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task Update_SameRecordSameDate_Succeeds()
    {
        var create = await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1), "Jan1"));

        // Update without changing the date — must not trip BR-1 against itself.
        var update = await CreateService().UpdateAsync(create.Value!.Id, new UpdateHolidayRequest
        {
            Name = "Jan1 Renamed",
            Date = new DateOnly(2026, 1, 1),
            Type = "Public",
        });

        update.IsSuccess.Should().BeTrue();
        update.Value!.Name.Should().Be("Jan1 Renamed");
    }

    // ── Deactivate (BR-4) ──────────────────────────────────────────

    [Fact]
    public async Task Deactivate_ActiveHoliday_Succeeds()
    {
        var create = await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1)));

        var deactivate = await CreateService().DeactivateAsync(create.Value!.Id);
        deactivate.IsSuccess.Should().BeTrue();

        var fetched = await CreateService().GetByIdAsync(create.Value.Id);
        fetched.Value!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_AlreadyInactive_Fails()
    {
        var create = await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1)));
        (await CreateService().DeactivateAsync(create.Value!.Id)).IsSuccess.Should().BeTrue();

        var again = await CreateService().DeactivateAsync(create.Value.Id);
        again.IsFailure.Should().BeTrue();
        again.Error.Should().Contain("already deactivated");
    }

    // ── Queries (FR-6, AC-4) ───────────────────────────────────────

    [Fact]
    public async Task GetAll_ByYear_FiltersToThatYear()
    {
        await CreateService().CreateAsync(Req(new DateOnly(2026, 6, 1), "In2026"));
        await CreateService().CreateAsync(Req(new DateOnly(2027, 6, 1), "In2027"));

        var result = await CreateService().GetAllAsync(null, null, 2026, null, null, default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle();
        result.Value[0].Name.Should().Be("In2026");
    }

    [Fact]
    public async Task GetAll_ByDateRange_FiltersInclusive()
    {
        await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1), "Jan"));
        await CreateService().CreateAsync(Req(new DateOnly(2026, 6, 1), "Jun"));
        await CreateService().CreateAsync(Req(new DateOnly(2026, 12, 1), "Dec"));

        var result = await CreateService().GetAllAsync(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 9, 1), null, null, null, default);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(h => h.Name == "Jun");
    }

    [Fact]
    public async Task GetAll_ActiveOnly_ExcludesDeactivated()
    {
        var keep = await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1), "Keep"));
        var drop = await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 2), "Drop"));
        await CreateService().DeactivateAsync(drop.Value!.Id);

        var result = await CreateService().GetAllAsync(null, null, 2026, null, true, default);

        result.Value!.Should().ContainSingle(h => h.Id == keep.Value!.Id);
    }

    // ── CSV import (FR-4, AC-3, NFR-3) ─────────────────────────────

    private static Stream Csv(string content)
        => new MemoryStream(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task ImportCsv_ValidRows_AllCreated()
    {
        const string csv =
            "name,date,type\n" +
            "New Year's Day,2026-01-01,Public\n" +
            "Independence Day,2026-07-04,Public\n" +
            "Christmas Day,2026-12-25,Public\n";

        var result = await CreateService().ImportCsvAsync(Csv(csv), "holidays.csv");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Total.Should().Be(3);
        result.Value.Created.Should().Be(3);
        result.Value.Failed.Should().Be(0);

        var all = await CreateService().GetAllAsync(null, null, 2026, null, null, default);
        all.Value!.Should().HaveCount(3);
    }

    [Fact]
    public async Task ImportCsv_DuplicateWithinFile_FlaggedNotCreated()
    {
        const string csv =
            "name,date,type\n" +
            "First,2026-01-01,Public\n" +
            "Second on same day,2026-01-01,Public\n";

        var result = await CreateService().ImportCsvAsync(Csv(csv), "holidays.csv");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Created.Should().Be(1);
        result.Value.Failed.Should().Be(1);
        result.Value.Errors.Should().Contain(e => e.Error.Contains("Duplicate date"));
    }

    [Fact]
    public async Task ImportCsv_DuplicateOfExistingDbRow_Flagged()
    {
        // A holiday already exists on this date.
        (await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1), "Existing"))).IsSuccess.Should().BeTrue();

        const string csv =
            "name,date,type\n" +
            "Clashing,2026-01-01,Public\n" +
            "Fine,2026-02-01,Public\n";

        var result = await CreateService().ImportCsvAsync(Csv(csv), "holidays.csv");

        result.Value!.Created.Should().Be(1);
        result.Value.Failed.Should().Be(1);
        result.Value.Errors.Should().Contain(e => e.Error.Contains("already exists"));
    }

    [Fact]
    public async Task ImportCsv_InvalidDateAndType_FlaggedPerRow()
    {
        const string csv =
            "name,date,type\n" +
            ",2026-01-01,Public\n" +              // missing name
            "Bad date,not-a-date,Public\n" +      // invalid date
            "Bad type,2026-03-01,Nonsense\n";     // invalid type

        var result = await CreateService().ImportCsvAsync(Csv(csv), "holidays.csv");

        result.Value!.Created.Should().Be(0);
        result.Value.Failed.Should().Be(3);
        result.Value.Errors.Should().Contain(e => e.Field == "name");
        result.Value.Errors.Should().Contain(e => e.Field == "date");
        result.Value.Errors.Should().Contain(e => e.Field == "type");
    }

    [Fact]
    public async Task ImportCsv_ExceedsRowLimit_Fails()
    {
        var sb = new StringBuilder("name,date,type\n");
        // 101 rows, one per day, all valid -> exceeds the 100-row cap.
        var day = new DateOnly(2026, 1, 1);
        for (int i = 0; i <= HolidayService.MaxImportRows; i++)
            sb.Append($"H{i},{day.AddDays(i):yyyy-MM-dd},Public\n");

        var result = await CreateService().ImportCsvAsync(Csv(sb.ToString()), "holidays.csv");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("exceeding the maximum");
    }

    [Fact]
    public async Task ImportCsv_NonCsvExtension_Fails()
    {
        var result = await CreateService().ImportCsvAsync(Csv("name,date,type\n"), "holidays.xlsx");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Only .csv");
    }

    // ── Recurring generation (FR-3, BR-5) ──────────────────────────

    [Fact]
    public async Task GenerateRecurring_CreatesNextYearEntry()
    {
        // A recurring holiday in 2026.
        (await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1), "New Year's Day", isRecurring: true)))
            .IsSuccess.Should().BeTrue();
        // A non-recurring holiday in 2026 must NOT be carried forward.
        (await CreateService().CreateAsync(Req(new DateOnly(2026, 6, 15), "One-off", isRecurring: false)))
            .IsSuccess.Should().BeTrue();

        var gen = await CreateService().GenerateRecurringForYearAsync(_tenantId, 2027);

        gen.IsSuccess.Should().BeTrue();
        gen.Value.Should().Be(1);

        var year2027 = await CreateService().GetAllAsync(null, null, 2027, null, null, default);
        year2027.Value!.Should().ContainSingle(h => h.Date == new DateOnly(2027, 1, 1) && h.Name == "New Year's Day");
    }

    [Fact]
    public async Task GenerateRecurring_IsIdempotent()
    {
        (await CreateService().CreateAsync(Req(new DateOnly(2026, 1, 1), "New Year's Day", isRecurring: true)))
            .IsSuccess.Should().BeTrue();

        var first = await CreateService().GenerateRecurringForYearAsync(_tenantId, 2027);
        first.Value.Should().Be(1);

        // Re-running for the same target year creates nothing more (idempotent skip).
        var second = await CreateService().GenerateRecurringForYearAsync(_tenantId, 2027);
        second.Value.Should().Be(0);

        var year2027 = await CreateService().GetAllAsync(null, null, 2027, null, null, default);
        year2027.Value!.Should().HaveCount(1);
    }

    [Fact]
    public void AddYearSafe_Feb29_ClampsToFeb28OnNonLeapYear()
    {
        var result = HolidayService.AddYearSafe(new DateOnly(2028, 2, 29), 1); // 2029 is not a leap year
        result.Should().Be(new DateOnly(2029, 2, 28));
    }
}
