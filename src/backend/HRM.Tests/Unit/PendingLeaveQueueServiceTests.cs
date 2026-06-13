// ============================================================================
// US-LV-004: Pending Leave Queue (manager view) unit tests.
// Covers: direct-report scope (BR-1), empty page when user has no employee record,
// pagination math (FR-4), overdue flag (BR-3), team-conflict count (FR-5),
// filter application (FR-3 leave type / employee / date range), and sorting (FR-3).
// Uses EF Core InMemory provider (mirrors LeaveRequestServiceTests).
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

public sealed class PendingLeaveQueueServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _managerUser;
    private readonly IHolidayProvider _holidayProvider;
    private readonly ILeaveNotificationService _notificationService;
    private readonly ILogger<LeaveRequestService> _logger;

    private Guid _annualLeaveTypeId;
    private Guid _sickLeaveTypeId;
    private Guid _managerEmployeeId;
    private Guid _report1Id;   // direct report of the manager
    private Guid _report2Id;   // direct report of the manager
    private Guid _outsiderId;  // reports to someone else

    public PendingLeaveQueueServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _managerUser = Substitute.For<ICurrentUser>();
        _managerUser.UserId.Returns(_managerUserId);

        _holidayProvider = new NoOpHolidayProvider();
        _notificationService = Substitute.For<ILeaveNotificationService>();
        _logger = Substitute.For<ILogger<LeaveRequestService>>();

        SeedReferenceData();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private LeaveRequestService CreateService(ICurrentUser? user = null)
        => new(CreateDbContext(), _tenantContext, user ?? _managerUser,
            _holidayProvider, _notificationService, _logger);

    private void SeedReferenceData()
    {
        using var db = CreateDbContext();

        db.LeaveTypes.AddRange(
            new LeaveType
            {
                Id = _annualLeaveTypeId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Annual Leave", Color = "#4CAF50", AnnualEntitlement = 14,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All, IsActive = true,
            },
            new LeaveType
            {
                Id = _sickLeaveTypeId = Guid.NewGuid(), TenantId = _tenantId,
                Name = "Sick Leave", Color = "#F44336", AnnualEntitlement = 7,
                AccrualFrequency = AccrualFrequency.Upfront, Gender = LeaveTypeGender.All, IsActive = true,
            });

        var manager = NewEmployee(_managerEmployeeId = Guid.NewGuid(), "Mary", "Manager", _managerUserId, reportsTo: null);
        var report1 = NewEmployee(_report1Id = Guid.NewGuid(), "Rita", "Report", null, reportsTo: _managerEmployeeId);
        var report2 = NewEmployee(_report2Id = Guid.NewGuid(), "Raj", "Report", null, reportsTo: _managerEmployeeId);
        var outsider = NewEmployee(_outsiderId = Guid.NewGuid(), "Otto", "Outside", null, reportsTo: Guid.NewGuid());

        db.Employees.AddRange(manager, report1, report2, outsider);
        db.SaveChanges();
    }

    private Employee NewEmployee(Guid id, string first, string last, Guid? userId, Guid? reportsTo) => new()
    {
        Id = id,
        TenantId = _tenantId,
        UserId = userId,
        EmployeeNo = $"EMP-{id.ToString()[..4]}",
        FirstName = first,
        LastName = last,
        Email = $"{first}@test.com".ToLowerInvariant(),
        ProfilePhotoUrl = $"https://cdn/{first}.png".ToLowerInvariant(),
        DateOfJoining = new DateTime(2020, 1, 1),
        DepartmentId = Guid.NewGuid(),
        JobTitleId = Guid.NewGuid(),
        EmploymentType = EmploymentType.FullTime,
        Status = EmployeeStatus.Active,
        ReportsToEmployeeId = reportsTo,
        IsActive = true,
    };

    private void SeedBalance(Guid employeeId, Guid leaveTypeId, int year, decimal balance)
    {
        using var db = CreateDbContext();
        db.LeaveLedgerEntries.Add(new LeaveLedger
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EntryType = LedgerEntryType.Accrual,
            EmployeeId = employeeId, LeaveTypeId = leaveTypeId, LeaveYear = year,
            Amount = balance, BalanceAfter = balance, OccurredAt = DateTime.UtcNow,
        });
        db.SaveChanges();
    }

    private Guid SeedRequest(
        Guid employeeId, Guid leaveTypeId, DateOnly start, DateOnly end,
        LeaveRequestStatus status = LeaveRequestStatus.Pending,
        DateTime? requestedAt = null, decimal totalDays = 1m,
        IReadOnlyList<string>? attachments = null)
    {
        using var db = CreateDbContext();
        var id = BaseEntity.NewUuidV7();
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = id, TenantId = _tenantId, EmployeeId = employeeId, LeaveTypeId = leaveTypeId,
            StartDate = start, EndDate = end, TotalDays = totalDays, Status = status,
            RequestedAt = requestedAt ?? DateTime.UtcNow,
            AttachmentUrls = attachments?.ToList() ?? new(),
        });
        db.SaveChanges();
        return id;
    }

    private static PendingLeaveQueueQueryParams Params(
        Guid? leaveTypeId = null, Guid? employeeId = null,
        DateOnly? startDate = null, DateOnly? endDate = null,
        string? sortBy = null, bool sortAscending = true,
        int page = 1, int pageSize = 20) => new()
    {
        LeaveTypeId = leaveTypeId,
        EmployeeId = employeeId,
        StartDate = startDate,
        EndDate = endDate,
        SortBy = sortBy,
        SortAscending = sortAscending,
        Page = page,
        PageSize = pageSize,
    };

    private static readonly DateOnly Base = new(2026, 6, 1);

    // ── BR-1: scope — only direct reports ──────────────────────────

    [Fact]
    public async Task GetPending_ReturnsOnlyDirectReportRequests()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(1));
        SeedRequest(_report2Id, _annualLeaveTypeId, Base, Base.AddDays(1));
        SeedRequest(_outsiderId, _annualLeaveTypeId, Base, Base.AddDays(1)); // not Mary's report

        var result = await CreateService().GetPendingForManagerAsync(Params());

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(2);
        result.Value.Items.Should().OnlyContain(i => i.EmployeeId == _report1Id || i.EmployeeId == _report2Id);
    }

    [Fact]
    public async Task GetPending_ExcludesNonPendingRequests()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(1), LeaveRequestStatus.Pending);
        SeedRequest(_report1Id, _annualLeaveTypeId, Base.AddDays(10), Base.AddDays(11), LeaveRequestStatus.Approved);
        SeedRequest(_report1Id, _annualLeaveTypeId, Base.AddDays(20), Base.AddDays(21), LeaveRequestStatus.Rejected);

        var result = await CreateService().GetPendingForManagerAsync(Params());

        result.Value!.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPending_UserWithNoEmployeeRecord_ReturnsEmptyPage()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(1));

        var stranger = Substitute.For<ICurrentUser>();
        stranger.UserId.Returns(Guid.NewGuid());

        var result = await CreateService(stranger).GetPendingForManagerAsync(Params());

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(0);
        result.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPending_ManagerWithNoReports_ReturnsEmptyPage()
    {
        // outsider's manager is a random GUID with no employee row; act as that manager via a
        // fresh employee that has no direct reports.
        var lonelyUserId = Guid.NewGuid();
        using (var db = CreateDbContext())
        {
            db.Employees.Add(NewEmployee(Guid.NewGuid(), "Lon", "Ely", lonelyUserId, reportsTo: null));
            db.SaveChanges();
        }
        var lonely = Substitute.For<ICurrentUser>();
        lonely.UserId.Returns(lonelyUserId);

        var result = await CreateService(lonely).GetPendingForManagerAsync(Params());

        result.IsSuccess.Should().BeTrue();
        result.Value!.TotalCount.Should().Be(0);
    }

    // ── FR-4: pagination ───────────────────────────────────────────

    [Fact]
    public async Task GetPending_Paginates_Page1And2()
    {
        // 25 pending requests from report1, staggered requestedAt so ordering is deterministic.
        for (int i = 0; i < 25; i++)
            SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(1),
                requestedAt: DateTime.UtcNow.AddMinutes(i));

        var page1 = await CreateService().GetPendingForManagerAsync(Params(page: 1, pageSize: 20));
        var page2 = await CreateService().GetPendingForManagerAsync(Params(page: 2, pageSize: 20));

        page1.Value!.Items.Should().HaveCount(20);
        page1.Value.TotalCount.Should().Be(25);
        page2.Value!.Items.Should().HaveCount(5);
        page2.Value.TotalCount.Should().Be(25);
    }

    [Fact]
    public async Task GetPending_CapsPageSizeAt50()
    {
        var result = await CreateService().GetPendingForManagerAsync(Params(pageSize: 500));
        result.Value!.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task GetPending_DefaultsInvalidPagingToSafeValues()
    {
        var result = await CreateService().GetPendingForManagerAsync(Params(page: 0, pageSize: 0));
        result.Value!.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
    }

    // ── BR-3: overdue flag ─────────────────────────────────────────

    [Fact]
    public async Task GetPending_FlagsRequestsOlderThan30Days_AsOverdue()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(1),
            requestedAt: DateTime.UtcNow.AddDays(-31));
        SeedRequest(_report2Id, _annualLeaveTypeId, Base, Base.AddDays(1),
            requestedAt: DateTime.UtcNow.AddDays(-5));

        var result = await CreateService().GetPendingForManagerAsync(Params());

        var overdue = result.Value!.Items.Single(i => i.EmployeeId == _report1Id);
        var fresh = result.Value.Items.Single(i => i.EmployeeId == _report2Id);
        overdue.IsOverdue.Should().BeTrue();
        fresh.IsOverdue.Should().BeFalse();
    }

    // ── FR-5: team conflict count ──────────────────────────────────

    [Fact]
    public async Task GetPending_CountsTeamMembersWithOverlappingApprovedLeave()
    {
        // report1 has a PENDING request Jun1-Jun5.
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(4), LeaveRequestStatus.Pending);
        // report2 (same team) has an APPROVED leave Jun3-Jun7 — overlaps.
        SeedRequest(_report2Id, _annualLeaveTypeId, Base.AddDays(2), Base.AddDays(6), LeaveRequestStatus.Approved);

        var result = await CreateService().GetPendingForManagerAsync(Params());

        var row = result.Value!.Items.Single(i => i.EmployeeId == _report1Id);
        row.TeamConflictCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPending_ConflictCount_ExcludesRequesterOwnApprovedLeaveAndNonOverlap()
    {
        // report1 PENDING Jun1-Jun3.
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(2), LeaveRequestStatus.Pending);
        // report1's OWN approved leave (must not count as a conflict against itself).
        SeedRequest(_report1Id, _annualLeaveTypeId, Base.AddDays(1), Base.AddDays(1), LeaveRequestStatus.Approved);
        // report2 approved but NON-overlapping (Jun10-Jun12).
        SeedRequest(_report2Id, _annualLeaveTypeId, Base.AddDays(9), Base.AddDays(11), LeaveRequestStatus.Approved);

        var result = await CreateService().GetPendingForManagerAsync(Params());

        var row = result.Value!.Items.Single(i => i.EmployeeId == _report1Id);
        row.TeamConflictCount.Should().Be(0);
    }

    // ── FR-3: filters ──────────────────────────────────────────────

    [Fact]
    public async Task GetPending_FilterByLeaveType()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(1));
        SeedRequest(_report2Id, _sickLeaveTypeId, Base, Base.AddDays(1));

        var result = await CreateService().GetPendingForManagerAsync(Params(leaveTypeId: _sickLeaveTypeId));

        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Single().LeaveTypeId.Should().Be(_sickLeaveTypeId);
    }

    [Fact]
    public async Task GetPending_FilterByEmployee()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(1));
        SeedRequest(_report2Id, _annualLeaveTypeId, Base, Base.AddDays(1));

        var result = await CreateService().GetPendingForManagerAsync(Params(employeeId: _report2Id));

        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Single().EmployeeId.Should().Be(_report2Id);
    }

    [Fact]
    public async Task GetPending_FilterByDateRange_KeepsOnlyOverlapping()
    {
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(2));            // Jun1-3
        SeedRequest(_report2Id, _annualLeaveTypeId, Base.AddDays(20), Base.AddDays(22)); // Jun21-23

        // Window Jun1-Jun10 should keep only the first.
        var result = await CreateService().GetPendingForManagerAsync(
            Params(startDate: Base, endDate: Base.AddDays(9)));

        result.Value!.TotalCount.Should().Be(1);
        result.Value.Items.Single().EmployeeId.Should().Be(_report1Id);
    }

    // ── FR-3: sort + FR-2 projection ───────────────────────────────

    [Fact]
    public async Task GetPending_DefaultSort_OldestRequestedFirst()
    {
        var newer = SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(1),
            requestedAt: DateTime.UtcNow.AddMinutes(-1));
        var older = SeedRequest(_report2Id, _annualLeaveTypeId, Base, Base.AddDays(1),
            requestedAt: DateTime.UtcNow.AddMinutes(-10));

        var result = await CreateService().GetPendingForManagerAsync(Params());

        result.Value!.Items[0].RequestId.Should().Be(older);
        result.Value.Items[1].RequestId.Should().Be(newer);
    }

    [Fact]
    public async Task GetPending_ProjectsBalanceNamePhotoColorAndAttachments()
    {
        SeedBalance(_report1Id, _annualLeaveTypeId, Base.Year, 9.5m);
        SeedRequest(_report1Id, _annualLeaveTypeId, Base, Base.AddDays(1),
            attachments: new[] { "cert.pdf" });

        var result = await CreateService().GetPendingForManagerAsync(Params());

        var row = result.Value!.Items.Single();
        row.EmployeeName.Should().Be("Rita Report");
        row.EmployeePhoto.Should().Be("https://cdn/rita.png");
        row.LeaveTypeName.Should().Be("Annual Leave");
        row.LeaveTypeColor.Should().Be("#4CAF50");
        row.CurrentBalance.Should().Be(9.5m);
        row.HasAttachments.Should().BeTrue();
    }
}
