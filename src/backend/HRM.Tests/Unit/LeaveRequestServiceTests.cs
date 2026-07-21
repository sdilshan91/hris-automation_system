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
        _currentUser.IsAuthenticated.Returns(true);

        _holidayProvider = new NoOpHolidayProvider();
        _notificationService = Substitute.For<ILeaveNotificationService>();
        _logger = Substitute.For<ILogger<LeaveRequestService>>();

        SeedReferenceData();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveRequestService CreateService()
        => new(CreateDbContext(), _tenantContext, _currentUser, _holidayProvider, _notificationService, _logger,
            new TenantLeaveYearResolver(CreateDbContext(), _tenantContext));

    // US-LV-011 AC-1: the confirm-LOP path needs the optional ILeaveTypeService to resolve the
    // system LOP leave type. The other unit tests construct the service with the original 6 args.
    private LeaveRequestService CreateServiceWithLop()
        => new(CreateDbContext(), _tenantContext, _currentUser, _holidayProvider, _notificationService, _logger,
            new TenantLeaveYearResolver(CreateDbContext(), _tenantContext),
            holidayService: null,
            leaveTypeService: new LeaveTypeService(
                CreateDbContext(), _tenantContext,
                Substitute.For<ICurrentUser>(), Substitute.For<ILogger<LeaveTypeService>>()));

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
            CreateDbContext(), _tenantContext, otherUser, _holidayProvider, _notificationService, _logger,
            new TenantLeaveYearResolver(CreateDbContext(), _tenantContext));

        var monday = NextMonday();
        var result = await svc.CreateAsync(Req(_annualLeaveTypeId, monday, monday));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("No employee record");
    }

    // ── US-LV-011 AC-1: confirm-LOP on insufficient balance ─────────

    [Fact]
    public async Task Create_InsufficientBalance_NoNegative_NoConfirmLop_StillBlocked()
    {
        var monday = NextMonday();
        SeedBalance(_annualLeaveTypeId, monday.Year, 0m);

        var svc = CreateServiceWithLop();
        var req = Req(_annualLeaveTypeId, monday, monday.AddDays(4)); // 5 days, ConfirmLop defaults false
        var result = await svc.CreateAsync(req);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Insufficient leave balance");
        result.Error.Should().Contain("Loss of Pay (LOP)");
    }

    [Fact]
    public async Task Create_InsufficientBalance_ConfirmLop_CreatesLopRequest_NoBalanceDeduction()
    {
        var monday = NextMonday();
        SeedBalance(_annualLeaveTypeId, monday.Year, 0m);

        var svc = CreateServiceWithLop();
        var req = new CreateLeaveRequestRequest
        {
            LeaveTypeId = _annualLeaveTypeId,
            StartDate = monday,
            EndDate = monday.AddDays(4), // 5 working days
            Reason = "Family matter",
            ConfirmLop = true,
        };

        var result = await svc.CreateAsync(req);

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsLop.Should().BeTrue();
        result.Value.LopSource.Should().Be(LopSource.EmployeeRequest.ToString());

        using var db = CreateDbContext();
        var lop = db.LeaveRequests.Single(lr => lr.EmployeeId == _employeeId && lr.IsLop);
        lop.IsLop.Should().BeTrue();
        lop.LopSource.Should().Be(LopSource.EmployeeRequest);
        // BR-1: no balance deduction — no Used ledger row was written for this request.
        db.LeaveLedgerEntries.Any(l => l.LeaveRequestId == lop.Id).Should().BeFalse();
        // The LOP request is recorded against the system LOP leave type, not the requested type.
        lop.LeaveTypeId.Should().NotBe(_annualLeaveTypeId);
        db.LeaveTypes.Single(lt => lt.Id == lop.LeaveTypeId)
            .SystemCategory.Should().Be(LeaveTypeSystemCategory.LossOfPay);
    }

    // ── ISSUE-038: GetMine server-side history filters (US-LV-006 FR-6, TC-LV-120) ──

    private void SeedRequest(Guid leaveTypeId, DateOnly start, LeaveRequestStatus status)
    {
        using var db = CreateDbContext();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EmployeeId = _employeeId,
            LeaveTypeId = leaveTypeId,
            StartDate = start,
            EndDate = start,
            TotalDays = 1m,
            Status = status,
            RequestedAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task GetMine_NoFilter_ReturnsAll_ISSUE038()
    {
        SeedRequest(_annualLeaveTypeId, new DateOnly(2026, 3, 1), LeaveRequestStatus.Approved);
        SeedRequest(_sickLeaveTypeId, new DateOnly(2026, 5, 1), LeaveRequestStatus.Rejected);
        SeedRequest(_annualLeaveTypeId, new DateOnly(2027, 2, 1), LeaveRequestStatus.Pending);

        var result = await CreateService().GetMineAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetMine_StatusFilter_NarrowsToThatStatus_ISSUE038()
    {
        SeedRequest(_annualLeaveTypeId, new DateOnly(2026, 3, 1), LeaveRequestStatus.Approved);
        SeedRequest(_sickLeaveTypeId, new DateOnly(2026, 5, 1), LeaveRequestStatus.Rejected);
        SeedRequest(_annualLeaveTypeId, new DateOnly(2027, 2, 1), LeaveRequestStatus.Pending);

        var result = await CreateService().GetMineAsync(status: "Rejected");

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle();
        result.Value![0].Status.Should().Be("Rejected");
        result.Value![0].LeaveTypeId.Should().Be(_sickLeaveTypeId);
    }

    [Fact]
    public async Task GetMine_LeaveTypeFilter_NarrowsToThatType_ISSUE038()
    {
        SeedRequest(_annualLeaveTypeId, new DateOnly(2026, 3, 1), LeaveRequestStatus.Approved);
        SeedRequest(_sickLeaveTypeId, new DateOnly(2026, 5, 1), LeaveRequestStatus.Rejected);
        SeedRequest(_annualLeaveTypeId, new DateOnly(2027, 2, 1), LeaveRequestStatus.Pending);

        var result = await CreateService().GetMineAsync(leaveTypeId: _annualLeaveTypeId);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value!.Should().OnlyContain(r => r.LeaveTypeId == _annualLeaveTypeId);
    }

    [Fact]
    public async Task GetMine_YearFilter_NarrowsToThatStartYear_ISSUE038()
    {
        SeedRequest(_annualLeaveTypeId, new DateOnly(2026, 3, 1), LeaveRequestStatus.Approved);
        SeedRequest(_sickLeaveTypeId, new DateOnly(2026, 5, 1), LeaveRequestStatus.Rejected);
        SeedRequest(_annualLeaveTypeId, new DateOnly(2027, 2, 1), LeaveRequestStatus.Pending);

        var result = await CreateService().GetMineAsync(year: 2027);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle();
        result.Value![0].StartDate.Year.Should().Be(2027);
    }

    [Fact]
    public async Task GetMine_EchoesTenantCancellationWindow_DF54()
    {
        using (var db = CreateDbContext())
        {
            db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme", LeaveCancellationWindowDays = 5 });
            db.SaveChanges();
        }
        SeedRequest(_annualLeaveTypeId, new DateOnly(2027, 2, 1), LeaveRequestStatus.Approved);

        var result = await CreateService().GetMineAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().NotBeEmpty();
        result.Value!.Should().OnlyContain(r => r.CancellationWindowDays == 5);
    }

    [Fact]
    public async Task GetMine_CancellationWindowDefaultsToZero_WhenTenantRowAbsent_DF54()
    {
        SeedRequest(_annualLeaveTypeId, new DateOnly(2027, 2, 1), LeaveRequestStatus.Approved);

        var result = await CreateService().GetMineAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().OnlyContain(r => r.CancellationWindowDays == 0);
    }

    // ── ISSUE-035: eligible-types filters by BR-4 gender + BR-5 probation (TC-LV-058/059) ──

    [Fact]
    public async Task GetEligibleTypes_ExcludesGenderRestrictedType_ISSUE035()
    {
        // The seeded employee is Male; Maternity is Female-only and must NOT appear.
        var result = await CreateServiceWithLop().GetEligibleLeaveTypesAsync();

        result.IsSuccess.Should().BeTrue();
        var names = result.Value!.Select(lt => lt.Name).ToList();
        names.Should().Contain("Annual Leave");
        names.Should().Contain("Sick Leave");
        names.Should().NotContain("Maternity Leave");
    }

    [Fact]
    public async Task GetEligibleTypes_OnProbation_ExcludesNonProbationEligibleType_ISSUE035()
    {
        using (var db = CreateDbContext())
        {
            var emp = db.Employees.Find(_employeeId)!;
            emp.Status = EmployeeStatus.Probation;
            db.SaveChanges();
        }

        var result = await CreateServiceWithLop().GetEligibleLeaveTypesAsync();

        result.IsSuccess.Should().BeTrue();
        var names = result.Value!.Select(lt => lt.Name).ToList();
        // Sick is probation-eligible → shown; Annual is not → hidden; Maternity gender-excluded anyway.
        names.Should().Contain("Sick Leave");
        names.Should().NotContain("Annual Leave");
        names.Should().NotContain("Maternity Leave");
    }

    // ── ISSUE-042: team-calendar default range + max-span cap (US-LV-009 FR-1, TC-LV-182) ──

    [Fact]
    public async Task TeamCalendar_OmittedRange_DefaultsToCurrentMonth_ISSUE042()
    {
        var result = await CreateService().GetTeamLeaveCalendarAsync(new TeamLeaveCalendarQueryParams());

        result.IsSuccess.Should().BeTrue();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        result.Value!.From.Should().Be(monthStart);
        result.Value!.To.Should().Be(monthStart.AddMonths(1).AddDays(-1));
    }

    [Fact]
    public async Task TeamCalendar_ExcessiveSpan_RejectedWith400_ISSUE042()
    {
        var result = await CreateService().GetTeamLeaveCalendarAsync(new TeamLeaveCalendarQueryParams
        {
            From = new DateOnly(2020, 1, 1),
            To = new DateOnly(2030, 1, 1),
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("invalid_date_range");
    }

    // ── ISSUE-037 (FR-7): approve/reject emit DISTINCT semantic audit actions ──

    /// <summary>
    /// Seeds a requester employee reporting to the seeded manager (_employeeId ← _userId) and a Pending leave
    /// request for that requester, so the current user can approve/reject it via the legacy direct-manager path.
    /// </summary>
    private async Task<Guid> SeedPendingRequestForApprovalAsync(Guid leaveTypeId, decimal balance)
    {
        using var db = CreateDbContext();
        var monday = NextMonday();

        var requesterId = Guid.NewGuid();
        db.Employees.Add(new Employee
        {
            Id = requesterId,
            TenantId = _tenantId,
            EmployeeNo = "EMP-0002",
            FirstName = "Reggie",
            LastName = "Report",
            Email = "reggie@test.com",
            Gender = Gender.Male,
            DateOfJoining = new DateTime(2021, 1, 1),
            DepartmentId = Guid.NewGuid(),
            JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime,
            Status = EmployeeStatus.Active,
            IsActive = true,
            ReportsToEmployeeId = _employeeId, // the seeded manager is linked to _userId (the current user)
        });

        var requestId = Guid.NewGuid();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = requestId,
            TenantId = _tenantId,
            EmployeeId = requesterId,
            LeaveTypeId = leaveTypeId,
            StartDate = monday,
            EndDate = monday.AddDays(1),
            TotalDays = 2m,
            Status = LeaveRequestStatus.Pending,
            Reason = "Test",
        });

        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EntryType = LedgerEntryType.Accrual,
            EmployeeId = requesterId,
            LeaveTypeId = leaveTypeId,
            LeaveYear = monday.Year,
            Amount = balance,
            BalanceAfter = balance,
            OccurredAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return requestId;
    }

    [Fact]
    [Trait("TC", "TC-LV-105")]
    public async Task Approve_WritesDistinctLeaveApprovedAuditAction_ISSUE037()
    {
        var requestId = await SeedPendingRequestForApprovalAsync(_annualLeaveTypeId, 14m);

        var result = await CreateService().ApproveAsync(requestId, "looks good");
        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();
        var audit = db.AuditLogs
            .Where(a => a.Action == "Leave.Approved" && a.ResourceId == requestId.ToString())
            .ToList();

        audit.Should().ContainSingle("approve must write a distinct Leave.Approved audit row (FR-7)");
        var row = audit[0];
        row.EventType.Should().Be("Leave.Approved");
        row.ResourceType.Should().Be("LeaveRequest");
        row.UserId.Should().Be(_userId);
        row.TenantId.Should().Be(_tenantId);
        row.Before.Should().Contain("Pending", "the before-snapshot records the pre-decision status");
        row.After.Should().Contain("Approved", "the after-snapshot records the labeled transition");
        row.After.Should().Contain("looks good", "the decision comment is captured");
    }

    [Fact]
    [Trait("TC", "TC-LV-105")]
    public async Task Reject_WritesDistinctLeaveRejectedAuditAction_ISSUE037()
    {
        var requestId = await SeedPendingRequestForApprovalAsync(_annualLeaveTypeId, 14m);

        var result = await CreateService().RejectAsync(requestId, "insufficient cover");
        result.IsSuccess.Should().BeTrue(result.Error);

        using var db = CreateDbContext();
        var audit = db.AuditLogs
            .Where(a => a.Action == "Leave.Rejected" && a.ResourceId == requestId.ToString())
            .ToList();

        audit.Should().ContainSingle("reject must write a distinct Leave.Rejected audit row (FR-7)");
        var row = audit[0];
        row.EventType.Should().Be("Leave.Rejected");
        row.UserId.Should().Be(_userId);
        row.Before.Should().Contain("Pending");
        row.After.Should().Contain("Rejected");
        row.After.Should().Contain("insufficient cover", "the rejection reason is captured");
    }
}
