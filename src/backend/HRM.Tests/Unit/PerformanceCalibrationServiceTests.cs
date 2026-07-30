// ============================================================================
// US-PRF-011: calibrated-rating model + cohort surface — service unit tests.
//
// Drives PerformanceCalibrationService (apply) + PerformanceDashboardService.GetCalibrationCohortAsync (read)
// through the real EF Core InMemory provider (so the tenant global query filters apply). Covers:
//   - §1 (load-bearing): applying a calibration NEVER overwrites the review's original FinalScore; the
//     original is recoverable from every history row; a second round records the previous calibrated value.
//   - §3: reason is mandatory (422); an unpermitted caller is refused (403); calibration disabled ⇒ 409.
//   - §2: the cohort returns each employee's original + calibrated + reviewer + department, and respects the
//     department filter.
//
// PROVIDER NOTE: the tenant-isolation arm (tenant A's calibration invisible to tenant B) is proven on REAL
// Postgres in RatingCalibrationIsolationPostgresTests (RLS/round-trip need a real engine); the behavioural
// arms above are InMemory through the real services.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace HRM.Tests.Unit;

public sealed class PerformanceCalibrationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _cycleNoCalibId = Guid.NewGuid();
    private readonly Guid _deptEng = Guid.NewGuid();
    private readonly Guid _deptSales = Guid.NewGuid();

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _managerEmployeeId = Guid.NewGuid();

    private readonly Guid _empA = Guid.NewGuid(); // Eng, review 4.0, reviewer = manager
    private readonly Guid _empB = Guid.NewGuid(); // Sales, review 2.0, reviewer = manager

    public PerformanceCalibrationServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        Seed();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private PerformanceCalibrationService CalibrationService(ICurrentUser user) => new(
        CreateDbContext(), _tenantContext, user,
        Substitute.For<IPayrollAuditLogger>(),
        Substitute.For<ILogger<PerformanceCalibrationService>>());

    private PerformanceDashboardService DashboardService(ICurrentUser user) => new(
        CreateDbContext(), _tenantContext, user,
        Substitute.For<IFileStorage>(),
        Substitute.For<ILogger<PerformanceDashboardService>>());

    private ICurrentUser HrUser()
    {
        var hr = Substitute.For<ICurrentUser>();
        hr.UserId.Returns(Guid.NewGuid());
        hr.IsAuthenticated.Returns(true);
        hr.Permissions.Returns(new[] { PermissionCatalog.Performance.ViewAll, PermissionCatalog.Performance.PublishAll });
        return hr;
    }

    private ICurrentUser ManagerUser()
    {
        var mgr = Substitute.For<ICurrentUser>();
        mgr.UserId.Returns(_managerUserId);
        mgr.IsAuthenticated.Returns(true);
        mgr.Permissions.Returns(new[] { PermissionCatalog.Performance.ViewTeam });
        return mgr;
    }

    private void Seed()
    {
        using var db = CreateDbContext();
        var now = DateTime.UtcNow;

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.Departments.Add(new Department { Id = _deptEng, TenantId = _tenantId, Name = "Engineering" });
        db.Departments.Add(new Department { Id = _deptSales, TenantId = _tenantId, Name = "Sales" });

        db.Employees.Add(new Employee
        {
            Id = _managerEmployeeId, TenantId = _tenantId, UserId = _managerUserId,
            EmployeeNo = "EMP-MGR", FirstName = "Grace", LastName = "Hopper", Email = "g@acme.com",
            DepartmentId = _deptEng, Status = EmployeeStatus.Active, IsActive = true,
            EmploymentType = EmploymentType.FullTime,
        });
        db.Employees.Add(new Employee
        {
            Id = _empA, TenantId = _tenantId, EmployeeNo = "EMP-A", FirstName = "Ada", LastName = "Lovelace",
            Email = "a@acme.com", DepartmentId = _deptEng, ReportsToEmployeeId = _managerEmployeeId,
            Status = EmployeeStatus.Active, IsActive = true, EmploymentType = EmploymentType.FullTime,
        });
        db.Employees.Add(new Employee
        {
            Id = _empB, TenantId = _tenantId, EmployeeNo = "EMP-B", FirstName = "Alan", LastName = "Turing",
            Email = "b@acme.com", DepartmentId = _deptSales, ReportsToEmployeeId = _managerEmployeeId,
            Status = EmployeeStatus.Active, IsActive = true, EmploymentType = EmploymentType.FullTime,
        });

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026", Status = AppraisalCycleStatus.Active,
            StartDate = now.AddDays(-90), EndDate = now.AddDays(10), RatingScaleMax = 5, SelfWeightPercent = 30,
            IsCalibrationEnabled = true,
        });
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleNoCalibId, TenantId = _tenantId, Name = "FY2025", Status = AppraisalCycleStatus.Active,
            StartDate = now.AddDays(-400), EndDate = now.AddDays(-40), RatingScaleMax = 5, SelfWeightPercent = 30,
            IsCalibrationEnabled = false,
        });

        foreach (var eid in new[] { _empA, _empB })
            db.CycleParticipants.Add(new CycleParticipant
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = eid,
            });

        // Manager reviews SUBMITTED with final scores + reviewer = the manager (for cohort reviewer column).
        db.ManagerReviews.Add(new ManagerReview
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _empA,
            ReviewerEmployeeId = _managerEmployeeId,
            Status = ManagerReviewStatus.Submitted, FinalScore = 4.0m, SubmittedAt = now,
            SignoffStatus = ReviewSignoffStatus.NotStarted,
        });
        db.ManagerReviews.Add(new ManagerReview
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _empB,
            ReviewerEmployeeId = _managerEmployeeId,
            Status = ManagerReviewStatus.Submitted, FinalScore = 2.0m, SubmittedAt = now,
            SignoffStatus = ReviewSignoffStatus.NotStarted,
        });

        db.SaveChanges();
    }

    private static PerformanceDashboardFilter Filter(Guid? cycleId = null, Guid? departmentId = null)
        => new() { CycleId = cycleId, DepartmentId = departmentId };

    // ── §1: original rating is never lost (load-bearing) ─────────────────

    [Fact]
    public async Task Apply_calibration_preserves_the_original_review_final_score()
    {
        var result = await CalibrationService(HrUser()).ApplyAsync(
            new ApplyCalibrationInput(_cycleId, _empA, CalibratedScore: 3.0m, Reason: "Peer-group normalization."));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.OriginalScore.Should().Be(4.0m);
        result.Value.CalibratedScore.Should().Be(3.0m);
        result.Value.PreviousCalibratedScore.Should().BeNull();

        // The review's OWN final score is untouched — this is the original, never overwritten.
        await using var verify = CreateDbContext();
        var review = verify.ManagerReviews.Single(r => r.CycleId == _cycleId && r.EmployeeId == _empA);
        review.FinalScore.Should().Be(4.0m);

        // A history row captured the original alongside the calibrated value.
        var calib = verify.RatingCalibrations.Single(c => c.CycleId == _cycleId && c.EmployeeId == _empA);
        calib.OriginalScore.Should().Be(4.0m);
        calib.CalibratedScore.Should().Be(3.0m);
    }

    [Fact]
    public async Task Second_calibration_round_keeps_original_and_records_previous()
    {
        await CalibrationService(HrUser()).ApplyAsync(
            new ApplyCalibrationInput(_cycleId, _empA, 3.0m, "Round 1."));
        var round2 = await CalibrationService(HrUser()).ApplyAsync(
            new ApplyCalibrationInput(_cycleId, _empA, 3.5m, "Round 2 after committee."));

        round2.IsSuccess.Should().BeTrue(round2.Error);
        round2.Value!.OriginalScore.Should().Be(4.0m);            // still the untouched review score
        round2.Value.PreviousCalibratedScore.Should().Be(3.0m);   // the prior round's calibrated value
        round2.Value.CalibratedScore.Should().Be(3.5m);

        await using var verify = CreateDbContext();
        verify.RatingCalibrations.Count(c => c.CycleId == _cycleId && c.EmployeeId == _empA).Should().Be(2);
        verify.ManagerReviews.Single(r => r.EmployeeId == _empA).FinalScore.Should().Be(4.0m);
    }

    // ── §3: reason mandatory; permission-gated; calibration must be enabled ──

    [Fact]
    public async Task Apply_without_reason_is_rejected()
    {
        var result = await CalibrationService(HrUser()).ApplyAsync(
            new ApplyCalibrationInput(_cycleId, _empA, 3.0m, Reason: "   "));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("reason_required");
    }

    [Fact]
    public async Task Apply_by_unpermitted_caller_is_forbidden()
    {
        var result = await CalibrationService(ManagerUser()).ApplyAsync(
            new ApplyCalibrationInput(_cycleId, _empA, 3.0m, "Manager tries to calibrate."));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Apply_on_calibration_disabled_cycle_is_rejected()
    {
        var result = await CalibrationService(HrUser()).ApplyAsync(
            new ApplyCalibrationInput(_cycleNoCalibId, _empA, 3.0m, "No calibration phase."));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("calibration_disabled");
    }

    [Fact]
    public async Task Apply_score_above_scale_max_is_rejected()
    {
        var result = await CalibrationService(HrUser()).ApplyAsync(
            new ApplyCalibrationInput(_cycleId, _empA, CalibratedScore: 9.0m, Reason: "Too high."));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(422);
        result.ErrorCode.Should().Be("score_out_of_range");
    }

    // ── §2: cohort surface ───────────────────────────────────────────────

    [Fact]
    public async Task Cohort_returns_original_calibrated_reviewer_and_department()
    {
        // Calibrate only empA.
        await CalibrationService(HrUser()).ApplyAsync(
            new ApplyCalibrationInput(_cycleId, _empA, 3.0m, "Normalized."));

        var result = await DashboardService(HrUser()).GetCalibrationCohortAsync(Filter(cycleId: _cycleId));

        result.IsSuccess.Should().BeTrue(result.Error);
        var rows = result.Value!.Rows;
        rows.Should().HaveCount(2);

        var rowA = rows.Single(r => r.EmployeeId == _empA);
        rowA.OriginalScore.Should().Be(4.0m);
        rowA.CalibratedScore.Should().Be(3.0m);
        rowA.ReviewerName.Should().Be("Grace Hopper");
        rowA.DepartmentName.Should().Be("Engineering");

        var rowB = rows.Single(r => r.EmployeeId == _empB);
        rowB.OriginalScore.Should().Be(2.0m);
        rowB.CalibratedScore.Should().BeNull();   // never calibrated
        rowB.ReviewerName.Should().Be("Grace Hopper");
        rowB.DepartmentName.Should().Be("Sales");
    }

    [Fact]
    public async Task Cohort_respects_the_department_filter()
    {
        var result = await DashboardService(HrUser())
            .GetCalibrationCohortAsync(Filter(cycleId: _cycleId, departmentId: _deptSales));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Rows.Should().ContainSingle()
            .Which.EmployeeId.Should().Be(_empB);
    }
}
