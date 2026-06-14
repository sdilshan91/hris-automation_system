// ============================================================================
// US-LV-007: Holiday calendar integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (MediatR pipeline + ITenantContext-driven global query filters):
//   - Create + list a holiday (AC-1 / AC-4)
//   - CSV import happy path (AC-3)
//   - Tenant isolation: Tenant A holidays are invisible to Tenant B (Test Hint)
//   - End-to-end: applying for Mon-Fri leave spanning a Wednesday public holiday
//     yields 4 days (AC-2) — proves the DB-backed HolidayProvider now feeds the
//     leave-day calculation that US-LV-003 left as a NoOp.
//
// Provider note (same as LeaveRequestIntegrationTests): the verify gate runs
// `dotnet test` on EF Core InMemory (no Docker/Postgres in the agent sandbox);
// PostgreSQL-specific schema is validated by the separate `migrations` CI job
// applying the generated migration to real PG. These tests register the REAL
// HolidayProvider (not the NoOp) so the holiday-exclusion path is exercised.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Holidays.Commands;
using HRM.Application.Features.Holidays.Queries;
using HRM.Application.Features.LeaveRequests.Commands;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Text;

namespace HRM.Tests.Integration;

public sealed class HolidayIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();

    private Guid _leaveTypeA;
    private Guid _employeeA;

    public HolidayIntegrationTests()
    {
        SeedTwoTenants();
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
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

    private IMediator BuildPipeline(Guid tenantId, Guid userId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Email.Returns("hr@test.com");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        // Real holiday service + the DB-backed provider (the US-LV-007 wiring).
        services.AddScoped<IHolidayService, HolidayService>();
        services.AddScoped<IHolidayProvider, HolidayProvider>();
        services.AddScoped<ILeaveNotificationService, LogOnlyLeaveNotificationService>();
        services.AddScoped<ILeaveRequestService, LeaveRequestService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(CreateHolidayCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedTwoTenants()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _leaveTypeA = Guid.NewGuid();
        _employeeA = Guid.NewGuid();

        db.LeaveTypes.Add(new LeaveType
        {
            Id = _leaveTypeA, TenantId = _tenantA, Name = "Annual Leave",
            AnnualEntitlement = 14, AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All, IsActive = true,
        });

        db.Employees.Add(new Employee
        {
            Id = _employeeA, TenantId = _tenantA, UserId = _userA,
            EmployeeNo = "A-1", FirstName = "Alice", LastName = "A", Email = "a@a.com",
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });

        db.SaveChanges();
    }

    private CreateHolidayCommand HolidayCmd(DateOnly date, string name = "Holiday", bool recurring = false)
        => new(name, date, "Public", null, null, recurring);

    // ── Create + list (AC-1 / AC-4) ────────────────────────────────

    [Fact]
    public async Task CreateThenList_ReturnsTheHoliday()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var created = await mediator.Send(HolidayCmd(new DateOnly(2026, 1, 1), "New Year's Day"));
        created.IsSuccess.Should().BeTrue();

        var list = await mediator.Send(new GetHolidaysQuery(null, null, 2026, null, null));
        list.IsSuccess.Should().BeTrue();
        list.Value!.Should().ContainSingle(h => h.Id == created.Value!.Id && h.Name == "New Year's Day");
    }

    // ── CSV import happy path (AC-3) ───────────────────────────────

    [Fact]
    public async Task ImportCsv_HappyPath_CreatesAllRows()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var sb = new StringBuilder("name,date,type\n");
        var day = new DateOnly(2026, 1, 1);
        for (int i = 0; i < 20; i++)
            sb.Append($"Holiday {i},{day.AddDays(i * 7):yyyy-MM-dd},Public\n");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
        var result = await mediator.Send(new ImportHolidaysCommand(stream, "holidays.csv"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Total.Should().Be(20);
        result.Value.Created.Should().Be(20);
        result.Value.Failed.Should().Be(0);

        var list = await mediator.Send(new GetHolidaysQuery(null, null, 2026, null, null));
        list.Value!.Should().HaveCount(20);
    }

    // ── Tenant isolation (Test Hint) ───────────────────────────────

    [Fact]
    public async Task HolidaysInTenantA_NotVisibleToTenantB()
    {
        var medA = BuildPipeline(_tenantA, _userA);
        (await medA.Send(HolidayCmd(new DateOnly(2026, 1, 1), "A-only"))).IsSuccess.Should().BeTrue();

        // Tenant B sees none of Tenant A's holidays.
        var medB = BuildPipeline(_tenantB, Guid.NewGuid());
        var listB = await medB.Send(new GetHolidaysQuery(null, null, 2026, null, null));

        listB.IsSuccess.Should().BeTrue();
        listB.Value!.Should().BeEmpty();
    }

    // ── End-to-end: leave application excludes a public holiday (AC-2) ─

    [Fact]
    public async Task LeaveApplication_SpanningWednesdayHoliday_Counts4Days()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        // Use a concrete Mon-Fri week and place a public holiday on the Wednesday.
        var monday = new DateOnly(2026, 6, 15);
        var wednesday = new DateOnly(2026, 6, 17);
        var friday = new DateOnly(2026, 6, 19);
        monday.DayOfWeek.Should().Be(DayOfWeek.Monday);
        wednesday.DayOfWeek.Should().Be(DayOfWeek.Wednesday);

        (await mediator.Send(HolidayCmd(wednesday, "Mid-week Holiday"))).IsSuccess.Should().BeTrue();

        // Seed a balance for the leave type so the request passes the balance check.
        SeedBalance(_tenantA, _employeeA, _leaveTypeA, 2026, 14m);

        var apply = await mediator.Send(new CreateLeaveRequestCommand(
            _leaveTypeA, monday, friday, false, null, "Holiday week", null));

        apply.IsSuccess.Should().BeTrue();
        // 5 weekdays minus the 1 public holiday on Wednesday = 4 leave days (AC-2).
        apply.Value!.TotalDays.Should().Be(4m);
    }

    private void SeedBalance(Guid tenant, Guid employee, Guid leaveType, int year, decimal balance)
    {
        var ctx = new MutableTenantContext { TenantId = tenant };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenant,
            EntryType = LedgerEntryType.Accrual, EmployeeId = employee, LeaveTypeId = leaveType,
            LeaveYear = year, Amount = balance, BalanceAfter = balance, OccurredAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }
}
