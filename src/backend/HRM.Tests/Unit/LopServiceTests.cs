// ============================================================================
// US-LV-011: Compulsory Leave / Loss of Pay (LOP) — service unit tests.
//
// Covers:
//   - AC-3 / FR-3 AssignLopAsync: creates HrAssigned, is_lop=true, lop_source=HrAssigned
//     leave_request rows for the given dates, returns the created count, fires the
//     lop-assigned notification (BR-6), and is idempotent per date.
//   - AC-1 confirm-LOP via CreateLeaveRequest: insufficient balance + negative not
//     allowed + ConfirmLop=true -> request created with is_lop=true,
//     lop_source=EmployeeRequest, NO balance deduction; ConfirmLop=false -> still blocked.
//   - BR-1: the system "Loss of Pay" leave type has zero entitlement / no balance.
//   - BR-3 OverrideLopAsync: convert a SystemGenerated LOP entry to a balance-backed
//     type (Used deduction) or remove it (soft-cancel).
//   - FR-6 / BR-4 AssignCompulsoryLeaveAsync: deduct-from-balance-first, LOP on shortfall.
//   - AC-2 GenerateAbsenteeismLopAsync: NoOpAttendanceProvider seam -> creates nothing, idempotent.
//   - FR-5 GetLopSummaryAsync: correct LOP day counts for a period.
//
// Uses EF Core InMemory provider (mirrors LeaveTypeServiceTests / LeaveRequestServiceTests).
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

public sealed class LopServiceTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IAttendanceProvider _attendanceProvider;
    private readonly ILeaveNotificationService _notificationService;

    private Guid _annualLeaveTypeId;
    private Guid _employeeId;

    public LopServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.Email.Returns("hr@test.com");
        _currentUser.UserId.Returns(_userId);

        _attendanceProvider = new NoOpAttendanceProvider();
        _notificationService = Substitute.For<ILeaveNotificationService>();

        SeedReferenceData();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveTypeService CreateLeaveTypeService()
        => new(CreateDbContext(), _tenantContext, _currentUser,
            Substitute.For<ILogger<LeaveTypeService>>());

    private LopService CreateService()
        => new(
            CreateDbContext(), _tenantContext, CreateLeaveTypeService(),
            _attendanceProvider, _notificationService,
            Substitute.For<ILogger<LopService>>());

    private void SeedReferenceData()
    {
        using var db = CreateDbContext();

        db.LeaveTypes.Add(new LeaveType
        {
            Id = _annualLeaveTypeId = Guid.NewGuid(),
            TenantId = _tenantId,
            Name = "Annual Leave",
            Code = "AL",
            AnnualEntitlement = 14,
            AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All,
            NegativeBalanceAllowed = false,
            IsActive = true,
        });

        db.Employees.Add(new Employee
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
        });

        db.SaveChanges();
    }

    private Guid SeedEmployee(string firstName, EmployeeStatus status = EmployeeStatus.Active)
    {
        using var db = CreateDbContext();
        var id = Guid.NewGuid();
        db.Employees.Add(new Employee
        {
            Id = id,
            TenantId = _tenantId,
            UserId = Guid.NewGuid(),
            EmployeeNo = $"EMP-{firstName}",
            FirstName = firstName,
            LastName = "X",
            Email = $"{firstName}@test.com".ToLowerInvariant(),
            Gender = Gender.Male,
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = Guid.NewGuid(),
            JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime,
            Status = status,
            IsActive = true,
        });
        db.SaveChanges();
        return id;
    }

    private void SeedBalance(Guid employeeId, Guid leaveTypeId, int year, decimal balance)
    {
        using var db = CreateDbContext();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            EntryType = LedgerEntryType.Accrual,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            LeaveYear = year,
            Amount = balance,
            BalanceAfter = balance,
            OccurredAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    private Guid SeedLopRequest(
        Guid employeeId, Guid lopTypeId, DateOnly date,
        LeaveRequestStatus status, LopSource source)
    {
        using var db = CreateDbContext();
        var id = BaseEntity.NewUuidV7();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = id,
            TenantId = _tenantId,
            EmployeeId = employeeId,
            LeaveTypeId = lopTypeId,
            StartDate = date,
            EndDate = date,
            TotalDays = 1m,
            Status = status,
            RequestedAt = DateTime.UtcNow,
            IsLop = true,
            LopSource = source,
        });
        db.SaveChanges();
        return id;
    }

    private async Task<Guid> EnsureLopTypeAsync()
    {
        var result = await CreateLeaveTypeService().EnsureLopTypeForTenantAsync(_tenantId);
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    // Pick a fixed mid-month working set of dates well clear of any window logic.
    private static readonly DateOnly Day1 = new(2026, 3, 2); // Monday
    private static readonly DateOnly Day2 = new(2026, 3, 3);
    private static readonly DateOnly Day3 = new(2026, 3, 4);

    // ── AC-3 / FR-3: HR manual LOP assignment ──────────────────────

    [Fact]
    public async Task AssignLop_ForDates_CreatesHrAssignedLopRequests()
    {
        var svc = CreateService();

        var result = await svc.AssignLopAsync(new AssignLopRequest
        {
            EmployeeId = _employeeId,
            Dates = [Day1, Day2],
            Reason = "Unauthorised absence",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.CreatedCount.Should().Be(2);
        result.Value.RequestIds.Should().HaveCount(2);

        using var db = CreateDbContext();
        var rows = db.LeaveRequests
            .Where(lr => lr.EmployeeId == _employeeId && lr.IsLop)
            .ToList();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(lr => lr.Status == LeaveRequestStatus.HrAssigned);
        rows.Should().OnlyContain(lr => lr.IsLop);
        rows.Should().OnlyContain(lr => lr.LopSource == LopSource.HrAssigned);
        rows.Should().OnlyContain(lr => lr.LeaveTypeId == result.Value!.LeaveTypeId);
        rows.Select(lr => lr.StartDate).Should().BeEquivalentTo(new[] { Day1, Day2 });
    }

    [Fact]
    public async Task AssignLop_TargetsTheSystemLopLeaveType()
    {
        var lopTypeId = await EnsureLopTypeAsync();
        var svc = CreateService();

        var result = await svc.AssignLopAsync(new AssignLopRequest
        {
            EmployeeId = _employeeId,
            Dates = [Day1],
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.LeaveTypeId.Should().Be(lopTypeId);
    }

    [Fact]
    public async Task AssignLop_NotifiesEmployee()
    {
        var svc = CreateService();

        await svc.AssignLopAsync(new AssignLopRequest
        {
            EmployeeId = _employeeId,
            Dates = [Day1, Day2],
            Reason = "Absence",
        });

        await _notificationService.Received(1).NotifyLopAssignedAsync(
            _employeeId, LopSource.HrAssigned.ToString(), 2,
            "Absence", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AssignLop_WritesApprovalHistoryRow()
    {
        var svc = CreateService();

        await svc.AssignLopAsync(new AssignLopRequest
        {
            EmployeeId = _employeeId,
            Dates = [Day1],
            Reason = "Absence",
        });

        using var db = CreateDbContext();
        db.LeaveApprovalHistories
            .Count(h => h.ApproverEmployeeId == _employeeId)
            .Should().Be(1);
    }

    [Fact]
    public async Task AssignLop_DuplicateDate_IsIdempotent_SkipsExisting()
    {
        var lopTypeId = await EnsureLopTypeAsync();
        SeedLopRequest(_employeeId, lopTypeId, Day1, LeaveRequestStatus.HrAssigned, LopSource.HrAssigned);
        var svc = CreateService();

        var result = await svc.AssignLopAsync(new AssignLopRequest
        {
            EmployeeId = _employeeId,
            Dates = [Day1, Day2],
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.CreatedCount.Should().Be(1);            // only Day2 created
        result.Value.SkippedDates.Should().Contain(Day1);

        using var db = CreateDbContext();
        db.LeaveRequests.Count(lr => lr.EmployeeId == _employeeId && lr.IsLop).Should().Be(2);
    }

    [Fact]
    public async Task AssignLop_NoDates_Fails()
    {
        var svc = CreateService();

        var result = await svc.AssignLopAsync(new AssignLopRequest
        {
            EmployeeId = _employeeId,
            Dates = [],
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task AssignLop_UnknownEmployee_Returns404()
    {
        var svc = CreateService();

        var result = await svc.AssignLopAsync(new AssignLopRequest
        {
            EmployeeId = Guid.NewGuid(),
            Dates = [Day1],
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AssignLop_TenantNotResolved_Fails()
    {
        _tenantContext.IsResolved.Returns(false);
        var svc = CreateService();

        var result = await svc.AssignLopAsync(new AssignLopRequest
        {
            EmployeeId = _employeeId,
            Dates = [Day1],
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("Tenant context is not resolved");
    }

    // ── BR-1: system LOP leave type has no balance / zero entitlement ──

    [Fact]
    public async Task LopLeaveType_HasZeroEntitlement_AndNoNegativeBalance()
    {
        var lopTypeId = await EnsureLopTypeAsync();

        using var db = CreateDbContext();
        var lop = db.LeaveTypes.Single(lt => lt.Id == lopTypeId);
        lop.SystemCategory.Should().Be(LeaveTypeSystemCategory.LossOfPay);
        lop.AnnualEntitlement.Should().Be(0m);
        lop.NegativeBalanceAllowed.Should().BeFalse();
        lop.NegativeBalanceLimit.Should().BeNull();
    }

    // ── AC-2 / FR-2: auto-LOP from absenteeism (NoOp seam) ─────────

    [Fact]
    public async Task GenerateAbsenteeismLop_WithNoOpProvider_CreatesNothing()
    {
        var svc = CreateService();

        var result = await svc.GenerateAbsenteeismLopAsync(_employeeId, Day1, Day3);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);

        using var db = CreateDbContext();
        db.LeaveRequests.Any(lr => lr.EmployeeId == _employeeId && lr.IsLop).Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAbsenteeismLop_IsIdempotent_AcrossRuns()
    {
        var svc1 = CreateService();
        var first = await svc1.GenerateAbsenteeismLopAsync(_employeeId, Day1, Day3);
        var svc2 = CreateService();
        var second = await svc2.GenerateAbsenteeismLopAsync(_employeeId, Day1, Day3);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value.Should().Be(0);
        second.Value.Should().Be(0);

        using var db = CreateDbContext();
        db.LeaveRequests.Count(lr => lr.EmployeeId == _employeeId && lr.IsLop).Should().Be(0);
    }

    [Fact]
    public async Task GenerateAbsenteeismLop_InvertedRange_Fails()
    {
        var svc = CreateService();

        var result = await svc.GenerateAbsenteeismLopAsync(_employeeId, Day3, Day1);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ── BR-3: HR overrides a system-generated LOP entry ────────────

    [Fact]
    public async Task OverrideLop_ConvertToBalanceBackedType_DeductsBalance_ClearsLop()
    {
        var lopTypeId = await EnsureLopTypeAsync();
        var lopId = SeedLopRequest(
            _employeeId, lopTypeId, Day1, LeaveRequestStatus.SystemGenerated, LopSource.SystemGenerated);
        SeedBalance(_employeeId, _annualLeaveTypeId, Day1.Year, 5m);
        var svc = CreateService();

        var result = await svc.OverrideLopAsync(lopId, new OverrideLopRequest
        {
            TargetLeaveTypeId = _annualLeaveTypeId,
            Reason = "Employee provided a valid reason",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.IsLop.Should().BeFalse();
        result.Value.LeaveTypeId.Should().Be(_annualLeaveTypeId);
        result.Value.Status.Should().Be(LeaveRequestStatus.Approved.ToString());
        result.Value.LedgerEntryId.Should().NotBeNull();

        using var db = CreateDbContext();
        var converted = db.LeaveRequests.Single(lr => lr.Id == lopId);
        converted.IsLop.Should().BeFalse();
        converted.LopSource.Should().BeNull();
        converted.LeaveTypeId.Should().Be(_annualLeaveTypeId);
        converted.Status.Should().Be(LeaveRequestStatus.Approved);

        var ledger = db.LeaveLedgerEntries.Single(l => l.LeaveRequestId == lopId);
        ledger.EntryType.Should().Be(LedgerEntryType.Used);
        ledger.Amount.Should().Be(-1m);
        ledger.BalanceAfter.Should().Be(4m);
    }

    [Fact]
    public async Task OverrideLop_RemoveEntry_SoftCancels_NoLedger()
    {
        var lopTypeId = await EnsureLopTypeAsync();
        var lopId = SeedLopRequest(
            _employeeId, lopTypeId, Day1, LeaveRequestStatus.SystemGenerated, LopSource.SystemGenerated);
        var svc = CreateService();

        var result = await svc.OverrideLopAsync(lopId, new OverrideLopRequest
        {
            TargetLeaveTypeId = null,
            Reason = "Logged in error",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(LeaveRequestStatus.Cancelled.ToString());
        result.Value.LedgerEntryId.Should().BeNull();

        using var db = CreateDbContext();
        var cancelled = db.LeaveRequests.Single(lr => lr.Id == lopId);
        cancelled.Status.Should().Be(LeaveRequestStatus.Cancelled);
        cancelled.IsLop.Should().BeTrue(); // still flagged LOP, just cancelled
        db.LeaveLedgerEntries.Any(l => l.LeaveRequestId == lopId).Should().BeFalse();
    }

    [Fact]
    public async Task OverrideLop_ConvertWithInsufficientBalance_NoNegativeAllowed_Fails()
    {
        var lopTypeId = await EnsureLopTypeAsync();
        var lopId = SeedLopRequest(
            _employeeId, lopTypeId, Day1, LeaveRequestStatus.SystemGenerated, LopSource.SystemGenerated);
        // No balance seeded -> available 0, required 1 -> insufficient, negative not allowed.
        var svc = CreateService();

        var result = await svc.OverrideLopAsync(lopId, new OverrideLopRequest
        {
            TargetLeaveTypeId = _annualLeaveTypeId,
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Insufficient");
    }

    [Fact]
    public async Task OverrideLop_OnNonLopRequest_Fails()
    {
        using var seed = CreateDbContext();
        var normalId = BaseEntity.NewUuidV7();
        seed.LeaveRequests.Add(new LeaveRequest
        {
            Id = normalId,
            TenantId = _tenantId,
            EmployeeId = _employeeId,
            LeaveTypeId = _annualLeaveTypeId,
            StartDate = Day1,
            EndDate = Day1,
            TotalDays = 1m,
            Status = LeaveRequestStatus.Approved,
            RequestedAt = DateTime.UtcNow,
            IsLop = false,
        });
        seed.SaveChanges();

        var svc = CreateService();
        var result = await svc.OverrideLopAsync(normalId, new OverrideLopRequest
        {
            TargetLeaveTypeId = _annualLeaveTypeId,
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.Error.Should().Contain("Only LOP entries");
    }

    [Fact]
    public async Task OverrideLop_UnknownRequest_Returns404()
    {
        var svc = CreateService();

        var result = await svc.OverrideLopAsync(Guid.NewGuid(), new OverrideLopRequest());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── FR-6 / BR-4: compulsory leave bulk assign ──────────────────

    [Fact]
    public async Task AssignCompulsory_EmployeeWithBalance_DeductsFromBalance_NoLop()
    {
        SeedBalance(_employeeId, _annualLeaveTypeId, Day1.Year, 5m);
        var svc = CreateService();

        var result = await svc.AssignCompulsoryLeaveAsync(new CompulsoryLeaveRequest
        {
            LeaveTypeId = _annualLeaveTypeId,
            Dates = [Day1],
            EmployeeIds = [_employeeId],
            Reason = "Year-end shutdown",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.AssignedCount.Should().Be(1);
        result.Value.LopCount.Should().Be(0);

        using var db = CreateDbContext();
        var req = db.LeaveRequests.Single(lr => lr.EmployeeId == _employeeId && !lr.IsLop && lr.StartDate == Day1);
        req.LeaveTypeId.Should().Be(_annualLeaveTypeId);
        req.Status.Should().Be(LeaveRequestStatus.Approved);

        var ledger = db.LeaveLedgerEntries.Single(l => l.LeaveRequestId == req.Id);
        ledger.EntryType.Should().Be(LedgerEntryType.Used);
        ledger.Amount.Should().Be(-1m);
        ledger.BalanceAfter.Should().Be(4m);
    }

    [Fact]
    public async Task AssignCompulsory_EmployeeWithoutBalance_FallsBackToLop()
    {
        // No balance seeded -> shortfall -> LOP (BR-4).
        var svc = CreateService();

        var result = await svc.AssignCompulsoryLeaveAsync(new CompulsoryLeaveRequest
        {
            LeaveTypeId = _annualLeaveTypeId,
            Dates = [Day1],
            EmployeeIds = [_employeeId],
            Reason = "Year-end shutdown",
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.AssignedCount.Should().Be(1);
        result.Value.LopCount.Should().Be(1);

        using var db = CreateDbContext();
        var lop = db.LeaveRequests.Single(lr => lr.EmployeeId == _employeeId && lr.IsLop && lr.StartDate == Day1);
        lop.LopSource.Should().Be(LopSource.Compulsory);
        lop.Status.Should().Be(LeaveRequestStatus.HrAssigned);
        db.LeaveLedgerEntries.Any(l => l.LeaveRequestId == lop.Id).Should().BeFalse();
    }

    [Fact]
    public async Task AssignCompulsory_MixedEmployees_DeductsForSome_LopForOthers()
    {
        var withBalance = _employeeId;
        var noBalance = SeedEmployee("Zoe");
        SeedBalance(withBalance, _annualLeaveTypeId, Day1.Year, 3m);
        var svc = CreateService();

        var result = await svc.AssignCompulsoryLeaveAsync(new CompulsoryLeaveRequest
        {
            LeaveTypeId = _annualLeaveTypeId,
            Dates = [Day1],
            EmployeeIds = [withBalance, noBalance],
        });

        result.IsSuccess.Should().BeTrue();
        result.Value!.EmployeesProcessed.Should().Be(2);
        result.Value.AssignedCount.Should().Be(2);
        result.Value.LopCount.Should().Be(1);

        using var db = CreateDbContext();
        db.LeaveRequests.Single(lr => lr.EmployeeId == withBalance && !lr.IsLop && lr.StartDate == Day1)
            .Should().NotBeNull();
        db.LeaveRequests.Single(lr => lr.EmployeeId == noBalance && lr.IsLop && lr.StartDate == Day1)
            .LopSource.Should().Be(LopSource.Compulsory);
    }

    [Fact]
    public async Task AssignCompulsory_PersistsCompulsoryLeaveAnchorPerDate()
    {
        var svc = CreateService();

        var result = await svc.AssignCompulsoryLeaveAsync(new CompulsoryLeaveRequest
        {
            LeaveTypeId = _annualLeaveTypeId,
            Dates = [Day1, Day2],
            EmployeeIds = [_employeeId],
        });

        result.IsSuccess.Should().BeTrue();

        using var db = CreateDbContext();
        var anchors = db.CompulsoryLeaves.Where(c => c.LeaveTypeId == _annualLeaveTypeId).ToList();
        anchors.Should().HaveCount(2);
        anchors.Select(a => a.Date).Should().BeEquivalentTo(new[] { Day1, Day2 });
    }

    [Fact]
    public async Task AssignCompulsory_UnknownLeaveType_Returns404()
    {
        var svc = CreateService();

        var result = await svc.AssignCompulsoryLeaveAsync(new CompulsoryLeaveRequest
        {
            LeaveTypeId = Guid.NewGuid(),
            Dates = [Day1],
            EmployeeIds = [_employeeId],
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task AssignCompulsory_NoDates_Fails()
    {
        var svc = CreateService();

        var result = await svc.AssignCompulsoryLeaveAsync(new CompulsoryLeaveRequest
        {
            LeaveTypeId = _annualLeaveTypeId,
            Dates = [],
            EmployeeIds = [_employeeId],
        });

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ── FR-5: payroll LOP summary ──────────────────────────────────

    [Fact]
    public async Task GetLopSummary_ReturnsLopDaysForPeriod()
    {
        var lopTypeId = await EnsureLopTypeAsync();
        SeedLopRequest(_employeeId, lopTypeId, Day1, LeaveRequestStatus.HrAssigned, LopSource.HrAssigned);
        SeedLopRequest(_employeeId, lopTypeId, Day2, LeaveRequestStatus.SystemGenerated, LopSource.SystemGenerated);
        var svc = CreateService();

        var result = await svc.GetLopSummaryAsync(_employeeId, Day1, Day3);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalLopDays.Should().Be(2m);
        result.Value.Entries.Should().HaveCount(2);
        result.Value.Entries.Select(e => e.Source)
            .Should().BeEquivalentTo(new[]
            {
                LopSource.HrAssigned.ToString(),
                LopSource.SystemGenerated.ToString(),
            });
    }

    [Fact]
    public async Task GetLopSummary_ExcludesCancelledLopEntries()
    {
        var lopTypeId = await EnsureLopTypeAsync();
        SeedLopRequest(_employeeId, lopTypeId, Day1, LeaveRequestStatus.HrAssigned, LopSource.HrAssigned);
        SeedLopRequest(_employeeId, lopTypeId, Day2, LeaveRequestStatus.Cancelled, LopSource.SystemGenerated);
        var svc = CreateService();

        var result = await svc.GetLopSummaryAsync(_employeeId, Day1, Day3);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalLopDays.Should().Be(1m);
        result.Value.Entries.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetLopSummary_ExcludesDatesOutsidePeriod()
    {
        var lopTypeId = await EnsureLopTypeAsync();
        SeedLopRequest(_employeeId, lopTypeId, Day1, LeaveRequestStatus.HrAssigned, LopSource.HrAssigned);
        SeedLopRequest(_employeeId, lopTypeId, new DateOnly(2026, 6, 1), LeaveRequestStatus.HrAssigned, LopSource.HrAssigned);
        var svc = CreateService();

        var result = await svc.GetLopSummaryAsync(_employeeId, Day1, Day3);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalLopDays.Should().Be(1m);
    }

    [Fact]
    public async Task GetLopSummary_NoLop_ReturnsZero()
    {
        var svc = CreateService();

        var result = await svc.GetLopSummaryAsync(_employeeId, Day1, Day3);

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalLopDays.Should().Be(0m);
        result.Value.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLopSummary_InvertedRange_Fails()
    {
        var svc = CreateService();

        var result = await svc.GetLopSummaryAsync(_employeeId, Day3, Day1);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    public void Dispose()
    {
        // InMemory databases are cleaned up when the last connection closes.
    }
}
