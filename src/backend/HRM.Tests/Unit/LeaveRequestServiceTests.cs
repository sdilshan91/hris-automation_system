// ============================================================================
// US-LV-003: Leave Request Service Unit Tests
// Covers working-days total, half-day (0.5), balance insufficiency + negative
// balance, overlap detection, document requirement, past/future window (BR-1/BR-2),
// max consecutive (BR-3), gender restriction (BR-4), probation (BR-5),
// notification seam (FR-6), and tenant isolation.
// Uses EF Core InMemory provider (mirrors LeaveEntitlementServiceTests).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.LeaveRequests.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class LeaveRequestServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IHolidayProvider _holidayProvider;
    private readonly ILeaveNotificationService _notificationService;
    private readonly ILogger<LeaveRequestService> _logger;

    private Guid _annualLeaveTypeId;
    private Guid _sickLeaveTypeId;
    private Guid _maternityLeaveTypeId;
    private Guid _employeeId;

    public LeaveRequestServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);

        _holidayProvider = new NoOpHolidayProvider();
        _notificationService = Substitute.For<ILeaveNotificationService>();
        _logger = Substitute.For<ILogger<LeaveRequestService>>();

        SeedReferenceData();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveRequestService CreateService()
        => new(CreateDbContext(), _tenantContext, _currentUser, _holidayProvider, _notificationService, _logger);

    private void SeedReferenceData()
    {
        using var db = CreateDbContext();

        var annual = new LeaveType
        {
            Id = _annualLeaveTypeId = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Annual Leave",
            AnnualEntitlement = 14,
            AccrualFrequency = AccrualFrequency.Upfront,
            HalfDayAllowed = true,
            Gender = LeaveTypeGender.All,
            IsActive = true,
        };

        var sick = new LeaveType
        {
            Id = _sickLeaveTypeId = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Sick Leave",
            AnnualEntitlement = 7,
            AccrualFrequency = AccrualFrequency.Upfront,
            DocumentsRequired = true,
            DocumentDayThreshold = 2,
            ProbationEligible = true,
            Gender = LeaveTypeGender.All,
            IsActive = true,
        };

        var maternity = new LeaveType
        {
            Id = _maternityLeaveTypeId = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Maternity Leave",
            AnnualEntitlement = 84,
            AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.Female,
            IsActive = true,
        };

        var employee = new Employee
        {
            Id = _employeeId = Guid.NewGuid(),
            TenantId = _tenantId,
            UserId = _userId,
            EmployeeNo = "EMP-0001",
            FirstName = "John",
            LastName = "Doe",
            Email = "john@test.com",
            Gender = Gender.Male,
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(),
            JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
        };

        db.LeaveTypes.AddRange(annual, sick, maternity);
        db.Employees.Add(employee);
        db.SaveChanges();
    }

    private void SeedBalance(Guid leaveTypeId, int year, decimal balance)
    {
        using var db = CreateDbContext();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EntryType = LedgerEntryType.Accrual,
            EmployeeId = _employeeId,
            LeaveTypeId = leaveTypeId,
            LeaveYear = year,
            Amount = balance,
            BalanceAfter = balance,
            OccurredAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    // Pick a Monday close to "today" so it sits inside the BR-1/BR-2 window and is a weekday.
    private static DateOnly NextMonday()
    {
        var d = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3);
        while (d.DayOfWeek != DayOfWeek.Monday)
            d = d.AddDays(1);
        return d;
    }

    private static CreateLeaveRequestRequest Req(
        Guid leaveTypeId, DateOnly start, DateOnly end,
        bool isHalfDay = false, string? session = null,
        IReadOnlyList<string>? attachments = null) => new()
        {
            LeaveTypeId = leaveTypeId,
            StartDate = start,
            EndDate = end,
            IsHalfDay = isHalfDay,
            HalfDaySession = session,
            Reason = "Test",
            Attachments = attachments,
        };

    // ── Happy path + total days ────────────────────────────────────

    [Fact]
    public async Task Create_WithSufficientBalance_SucceedsAndComputesWorkingDays()
    {
        var monday = NextMonday();
        SeedBalance(_annualLeaveTypeId, monday.Year, 14m);

        var svc = CreateService();
        // Mon..Fri = 5 working days.
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday.AddDays(4)));

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalDays.Should().Be(5m);
        result.Value.Status.Should().Be("Pending");
    }

    [Fact]
    public async Task Create_FiresLeaveRequestedNotification()
    {
        var monday = NextMonday();
        SeedBalance(_annualLeaveTypeId, monday.Year, 14m);

        var svc = CreateService();
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday.AddDays(2)));

        result.IsSuccess.Should().BeTrue();
        await _notificationService.Received(1).NotifyLeaveRequestedAsync(
            Arg.Any<Guid>(), _employeeId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_HalfDay_ProducesHalfDayTotal()
    {
        var monday = NextMonday();
        SeedBalance(_annualLeaveTypeId, monday.Year, 14m);

        var svc = CreateService();
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday, isHalfDay: true, session: "AM"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalDays.Should().Be(0.5m);
        result.Value.HalfDaySession.Should().Be("AM");
    }

    // ── AC-2: balance ──────────────────────────────────────────────

    [Fact]
    public async Task Create_InsufficientBalance_NoNegativeAllowed_Fails()
    {
        var monday = NextMonday();
        SeedBalance(_annualLeaveTypeId, monday.Year, 2m); // only 2 days available

        var svc = CreateService();
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday.AddDays(4))); // 5 days

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Insufficient leave balance");
    }

    [Fact]
    public async Task Create_InsufficientBalance_NegativeAllowedWithinLimit_Succeeds()
    {
        var monday = NextMonday();
        // Configure annual leave to allow negative balance up to 10 days.
        using (var db = CreateDbContext())
        {
            var lt = db.LeaveTypes.Find(_annualLeaveTypeId)!;
            lt.NegativeBalanceAllowed = true;
            lt.NegativeBalanceLimit = 10m;
            db.SaveChanges();
        }
        SeedBalance(_annualLeaveTypeId, monday.Year, 0m);

        var svc = CreateService();
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday.AddDays(4))); // 5 days -> -5

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_NegativeAllowed_ExceedsLimit_Fails()
    {
        var monday = NextMonday();
        using (var db = CreateDbContext())
        {
            var lt = db.LeaveTypes.Find(_annualLeaveTypeId)!;
            lt.NegativeBalanceAllowed = true;
            lt.NegativeBalanceLimit = 2m;
            db.SaveChanges();
        }
        SeedBalance(_annualLeaveTypeId, monday.Year, 0m);

        var svc = CreateService();
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday.AddDays(4))); // -5 > -2

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("negative balance limit");
    }

    // ── AC-3: document requirement ─────────────────────────────────

    [Fact]
    public async Task Create_SickLeaveAboveThreshold_NoAttachment_Fails()
    {
        var monday = NextMonday();
        SeedBalance(_sickLeaveTypeId, monday.Year, 7m);

        var svc = CreateService();
        // Mon..Thu = 4 days > threshold 2, no attachment.
        var result = await svc.CreateAsync(Req(_sickLeaveTypeId, monday, monday.AddDays(3)));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Medical certificate is required");
    }

    [Fact]
    public async Task Create_SickLeaveAboveThreshold_WithAttachment_Succeeds()
    {
        var monday = NextMonday();
        SeedBalance(_sickLeaveTypeId, monday.Year, 7m);

        var svc = CreateService();
        var result = await svc.CreateAsync(
            Req(_sickLeaveTypeId, monday, monday.AddDays(3), attachments: ["cert.pdf"]));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Create_SickLeaveAtOrBelowThreshold_NoAttachment_Succeeds()
    {
        var monday = NextMonday();
        SeedBalance(_sickLeaveTypeId, monday.Year, 7m);

        var svc = CreateService();
        // Mon..Tue = 2 days, not > threshold 2.
        var result = await svc.CreateAsync(Req(_sickLeaveTypeId, monday, monday.AddDays(1)));

        result.IsSuccess.Should().BeTrue();
    }

    // ── AC-5: overlap ──────────────────────────────────────────────

    [Fact]
    public async Task Create_OverlappingPendingRequest_Fails()
    {
        var monday = NextMonday();
        SeedBalance(_annualLeaveTypeId, monday.Year, 14m);

        var svc = CreateService();
        var first = await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday.AddDays(2)));
        first.IsSuccess.Should().BeTrue();

        var svc2 = CreateService();
        // Overlaps the first range (Tue inside Mon..Wed).
        var second = await svc2.CreateAsync(Req(_annualLeaveTypeId, monday.AddDays(1), monday.AddDays(4)));

        second.IsFailure.Should().BeTrue();
        second.Error.Should().Contain("already have a leave request for the selected dates");
    }

    // ── BR-1 / BR-2: date windows ──────────────────────────────────

    [Fact]
    public async Task Create_PastDateBeyondLookback_Fails()
    {
        var pastMonday = NextMonday().AddDays(-30);
        SeedBalance(_annualLeaveTypeId, pastMonday.Year, 14m);

        var svc = CreateService();
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, pastMonday, pastMonday));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("in the past");
    }

    [Fact]
    public async Task Create_FutureDateBeyondWindow_Fails()
    {
        var farMonday = NextMonday().AddDays(120);
        SeedBalance(_annualLeaveTypeId, farMonday.Year, 14m);

        var svc = CreateService();
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, farMonday, farMonday));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("in the future");
    }

    // ── BR-3: max consecutive ──────────────────────────────────────

    [Fact]
    public async Task Create_ExceedsMaxConsecutiveDays_Fails()
    {
        var monday = NextMonday();
        using (var db = CreateDbContext())
        {
            var lt = db.LeaveTypes.Find(_annualLeaveTypeId)!;
            lt.MaxConsecutiveDays = 3;
            db.SaveChanges();
        }
        SeedBalance(_annualLeaveTypeId, monday.Year, 14m);

        var svc = CreateService();
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday.AddDays(4))); // 5 > 3

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("maximum");
    }

    // ── BR-4: gender restriction ───────────────────────────────────

    [Fact]
    public async Task Create_MaleEmployee_MaternityLeave_Fails()
    {
        var monday = NextMonday();
        SeedBalance(_maternityLeaveTypeId, monday.Year, 84m);

        var svc = CreateService();
        var result = await svc.CreateAsync(Req(_maternityLeaveTypeId, monday, monday.AddDays(2)));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("not available for your profile");
    }

    // ── BR-5: probation ────────────────────────────────────────────

    [Fact]
    public async Task Create_ProbationEmployee_NonEligibleType_Fails()
    {
        var monday = NextMonday();
        using (var db = CreateDbContext())
        {
            var emp = db.Employees.Find(_employeeId)!;
            emp.Status = EmployeeStatus.Probation;
            db.SaveChanges();
        }
        SeedBalance(_annualLeaveTypeId, monday.Year, 14m); // annual is NOT probation-eligible

        var svc = CreateService();
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday.AddDays(2)));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("probation");
    }

    // ── GetMine ────────────────────────────────────────────────────

    [Fact]
    public async Task GetMine_ReturnsOnlyOwnRequests()
    {
        var monday = NextMonday();
        SeedBalance(_annualLeaveTypeId, monday.Year, 14m);

        var svc = CreateService();
        await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday.AddDays(2)));

        var listSvc = CreateService();
        var result = await listSvc.GetMineAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(1);
        result.Value[0].EmployeeId.Should().Be(_employeeId);
    }

    // ── Tenant isolation ───────────────────────────────────────────

    [Fact]
    public async Task Create_UserWithNoEmployeeInTenant_Fails()
    {
        // A user with no linked employee in this tenant cannot apply.
        var otherUser = Substitute.For<ICurrentUser>();
        otherUser.UserId.Returns(Guid.NewGuid());

        var svc = new LeaveRequestService(
            CreateDbContext(), _tenantContext, otherUser, _holidayProvider, _notificationService, _logger);

        var monday = NextMonday();
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("No employee record");
    }
}
