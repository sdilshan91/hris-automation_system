// ============================================================================
// US-PRF-007: Performance dashboard + analytics — service unit tests.
//
// Drives PerformanceDashboardService through the real EF Core InMemory provider (so the
// tenant global query filters apply). Covers:
//   - AC-1: average score + score-distribution histogram buckets over the seeded final scores.
//   - AC-1/FR-6: cycle progress (participants, goal-setting / self-assessment / manager-review /
//     signed-off counts + completion rate).
//   - FR-2: department-wise averages.
//   - FR-3/BR-3: top-N / bottom-N performers (HR) and the configurable count.
//   - FR-4/BR-2: filters combine (department, employment type) + probation exclusion.
//   - AC-5/BR-1/BR-3: a manager is hard-scoped to direct reports — team ranking, no bottom list,
//     and a non-report's data never appears; a permission-less caller is forbidden.
//   - FR-5: department drill-down lists the department's employees + scores.
//   - FR-8/AC-4: CSV + XLSX export are non-empty; an unknown format is rejected.
//
// NOTE ON PROVIDER: same rationale as the other dashboard tests — the verify gate runs `dotnet test`
// with no PostgreSQL/Docker, so InMemory through the real service proves the tenant + scope filters.
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

public sealed class PerformanceDashboardServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;

    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _deptEng = Guid.NewGuid();
    private readonly Guid _deptSales = Guid.NewGuid();

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _managerEmployeeId = Guid.NewGuid();

    // Eng dept employees (report to the manager): scores 4.0, 2.0.
    private readonly Guid _empA = Guid.NewGuid();
    private readonly Guid _empB = Guid.NewGuid();
    // Sales dept employee (NOT a report of the manager): score 5.0.
    private readonly Guid _empC = Guid.NewGuid();

    public PerformanceDashboardServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        Seed();
    }

    private AppDbContext CreateDbContext() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private PerformanceDashboardService CreateService(ICurrentUser user) => new(
        CreateDbContext(), _tenantContext, user,
        Substitute.For<IFileStorage>(),
        Substitute.For<ILogger<PerformanceDashboardService>>());

    private ICurrentUser HrUser()
    {
        var hr = Substitute.For<ICurrentUser>();
        hr.UserId.Returns(Guid.NewGuid());
        hr.IsAuthenticated.Returns(true);
        hr.Permissions.Returns(new[] { PermissionCatalog.Performance.ViewAll });
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

    private ICurrentUser EmployeeUser()
    {
        var emp = Substitute.For<ICurrentUser>();
        emp.UserId.Returns(Guid.NewGuid());
        emp.IsAuthenticated.Returns(true);
        emp.Permissions.Returns(new[] { PermissionCatalog.Performance.ReadSelf });
        return emp;
    }

    private void Seed()
    {
        using var db = CreateDbContext();
        var now = DateTime.UtcNow;

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.Departments.Add(new Department { Id = _deptEng, TenantId = _tenantId, Name = "Engineering" });
        db.Departments.Add(new Department { Id = _deptSales, TenantId = _tenantId, Name = "Sales" });

        // Manager (Eng), reports to nobody.
        db.Employees.Add(new Employee
        {
            Id = _managerEmployeeId, TenantId = _tenantId, UserId = _managerUserId,
            EmployeeNo = "EMP-MGR", FirstName = "Grace", LastName = "Hopper", Email = "g@acme.com",
            DepartmentId = _deptEng, Status = EmployeeStatus.Active, IsActive = true,
            EmploymentType = EmploymentType.FullTime,
        });

        // Two Eng direct reports (scored 4.0 and 2.0).
        db.Employees.Add(new Employee
        {
            Id = _empA, TenantId = _tenantId, EmployeeNo = "EMP-A", FirstName = "Ada", LastName = "Lovelace",
            Email = "a@acme.com", DepartmentId = _deptEng, ReportsToEmployeeId = _managerEmployeeId,
            Status = EmployeeStatus.Active, IsActive = true, EmploymentType = EmploymentType.FullTime,
        });
        db.Employees.Add(new Employee
        {
            Id = _empB, TenantId = _tenantId, EmployeeNo = "EMP-B", FirstName = "Alan", LastName = "Turing",
            Email = "b@acme.com", DepartmentId = _deptEng, ReportsToEmployeeId = _managerEmployeeId,
            Status = EmployeeStatus.Active, IsActive = true, EmploymentType = EmploymentType.Contract,
        });

        // Sales employee (NOT a report of the manager), scored 5.0.
        db.Employees.Add(new Employee
        {
            Id = _empC, TenantId = _tenantId, EmployeeNo = "EMP-C", FirstName = "Katherine", LastName = "Johnson",
            Email = "c@acme.com", DepartmentId = _deptSales, ReportsToEmployeeId = Guid.NewGuid(),
            Status = EmployeeStatus.Active, IsActive = true, EmploymentType = EmploymentType.FullTime,
        });

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026", Status = AppraisalCycleStatus.Active,
            StartDate = now.AddDays(-90), EndDate = now.AddDays(10), RatingScaleMax = 5, SelfWeightPercent = 30,
        });

        // Participants: A, B, C enrolled.
        foreach (var eid in new[] { _empA, _empB, _empC })
            db.CycleParticipants.Add(new CycleParticipant
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = eid,
            });

        // Goals: A + B have goals (goal-setting complete); C does not.
        foreach (var eid in new[] { _empA, _empB })
            db.Goals.Add(new Goal
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = eid,
                Title = "Ship", Weight = 100, Category = GoalCategory.Kpi, Status = GoalStatus.Submitted,
                TargetValue = "1", MeasurementUnit = "x", DueDate = DateOnly.FromDateTime(now),
            });

        // Self-assessments submitted for A + C; B not submitted.
        foreach (var eid in new[] { _empA, _empC })
            db.SelfAssessments.Add(new SelfAssessment
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = eid,
                Status = SelfAssessmentStatus.Submitted, WeightedSelfScore = 4m, SubmittedAt = now,
            });

        // Manager reviews — SUBMITTED with final scores: A=4.0 (signed off), B=2.0, C=5.0.
        db.ManagerReviews.Add(new ManagerReview
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _empA,
            Status = ManagerReviewStatus.Submitted, FinalScore = 4.0m, SubmittedAt = now,
            SignoffStatus = ReviewSignoffStatus.SignedOff,
        });
        db.ManagerReviews.Add(new ManagerReview
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _empB,
            Status = ManagerReviewStatus.Submitted, FinalScore = 2.0m, SubmittedAt = now,
            SignoffStatus = ReviewSignoffStatus.NotStarted,
        });
        db.ManagerReviews.Add(new ManagerReview
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _empC,
            Status = ManagerReviewStatus.Submitted, FinalScore = 5.0m, SubmittedAt = now,
            SignoffStatus = ReviewSignoffStatus.NotStarted,
        });

        db.SaveChanges();
    }

    private static PerformanceDashboardFilter Filter(
        Guid? cycleId = null, Guid? departmentId = null, string? employmentType = null,
        int topBottom = 10, bool includeProbation = false)
        => new()
        {
            CycleId = cycleId,
            DepartmentId = departmentId,
            EmploymentType = employmentType,
            TopBottomCount = topBottom,
            IncludeProbation = includeProbation,
        };

    // ══════════════════════════════════════════════════════════════
    //  HR overview — averages, distribution, departments (AC-1/FR-1/FR-2)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Hr_overview_averages_all_three_scored_employees()
    {
        var result = await CreateService(HrUser()).GetOverviewAsync(Filter(_cycleId));

        result.IsSuccess.Should().BeTrue(result.Error);
        var d = result.Value!;
        d.Scope.Should().Be("Organization");
        d.ScoredEmployeeCount.Should().Be(3);
        d.AverageScore.Should().Be(3.67m);            // (4 + 2 + 5) / 3 = 3.666… -> 3.67
        d.RatingScaleMax.Should().Be(5);
    }

    [Fact]
    public async Task Hr_overview_distribution_buckets_count_scores()
    {
        var d = (await CreateService(HrUser()).GetOverviewAsync(Filter(_cycleId))).Value!;

        var byLabel = d.ScoreDistribution.ToDictionary(b => b.Label, b => b.Count);
        // Buckets: 1–2, 2–3, 3–4, 4–5 (top inclusive). Scores 2.0, 4.0, 5.0.
        byLabel["2.0–3.0"].Should().Be(1);   // the 2.0
        byLabel["3.0–4.0"].Should().Be(0);
        byLabel["4.0–5.0"].Should().Be(2);   // 4.0 and 5.0 (top bucket inclusive of max)
        d.ScoreDistribution.Sum(b => b.Count).Should().Be(3);
    }

    [Fact]
    public async Task Hr_overview_department_averages_split_by_department()
    {
        var d = (await CreateService(HrUser()).GetOverviewAsync(Filter(_cycleId))).Value!;

        var byDept = d.DepartmentAverages.ToDictionary(x => x.DepartmentName, x => x);
        byDept["Engineering"].AverageScore.Should().Be(3.0m);   // (4 + 2) / 2
        byDept["Engineering"].Headcount.Should().Be(2);
        byDept["Sales"].AverageScore.Should().Be(5.0m);
        byDept["Sales"].Headcount.Should().Be(1);
    }

    // ══════════════════════════════════════════════════════════════
    //  Cycle progress (AC-1/FR-6)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Hr_overview_progress_counts_each_phase()
    {
        var d = (await CreateService(HrUser()).GetOverviewAsync(Filter(_cycleId))).Value!;

        var p = d.Progress;
        p.TotalParticipants.Should().Be(3);          // A, B, C
        p.GoalSettingCompleted.Should().Be(2);       // A, B have goals
        p.SelfAssessmentCompleted.Should().Be(2);    // A, C submitted
        p.ManagerReviewCompleted.Should().Be(3);     // all submitted
        p.SignedOff.Should().Be(1);                  // A signed off
        p.CompletionRate.Should().Be(100m);          // 3/3 manager reviews
    }

    // ══════════════════════════════════════════════════════════════
    //  Top / bottom performers (FR-3/BR-3)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Hr_top_and_bottom_performers_are_ordered()
    {
        var d = (await CreateService(HrUser()).GetOverviewAsync(Filter(_cycleId))).Value!;

        d.TopPerformers.First().Score.Should().Be(5.0m);     // Katherine (Sales)
        d.TopPerformers.First().EmployeeId.Should().Be(_empC);
        d.BottomPerformers.First().Score.Should().Be(2.0m);  // Alan (Eng)
        d.BottomPerformers.First().EmployeeId.Should().Be(_empB);
    }

    [Fact]
    public async Task Top_performer_count_is_configurable()
    {
        var d = (await CreateService(HrUser()).GetOverviewAsync(Filter(_cycleId, topBottom: 1))).Value!;

        d.TopPerformers.Should().HaveCount(1);
        d.BottomPerformers.Should().HaveCount(1);
    }

    // ══════════════════════════════════════════════════════════════
    //  Filters (FR-4/BR-2)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Department_filter_limits_to_that_department()
    {
        var d = (await CreateService(HrUser()).GetOverviewAsync(Filter(_cycleId, departmentId: _deptSales))).Value!;

        d.ScoredEmployeeCount.Should().Be(1);
        d.AverageScore.Should().Be(5.0m);
    }

    [Fact]
    public async Task Employment_type_filter_combines_with_cycle()
    {
        // Only B is a Contract employee (score 2.0).
        var d = (await CreateService(HrUser())
            .GetOverviewAsync(Filter(_cycleId, employmentType: "Contract"))).Value!;

        d.ScoredEmployeeCount.Should().Be(1);
        d.AverageScore.Should().Be(2.0m);
    }

    [Fact]
    public async Task Probation_employee_is_excluded_by_default_and_included_on_request()
    {
        // Flip Sales employee C to Probation.
        using (var db = CreateDbContext())
        {
            var c = db.Employees.Single(e => e.Id == _empC);
            c.Status = EmployeeStatus.Probation;
            db.SaveChanges();
        }

        var excluded = (await CreateService(HrUser()).GetOverviewAsync(Filter(_cycleId))).Value!;
        excluded.ScoredEmployeeCount.Should().Be(2);  // A + B only

        var included = (await CreateService(HrUser())
            .GetOverviewAsync(Filter(_cycleId, includeProbation: true))).Value!;
        included.ScoredEmployeeCount.Should().Be(3);  // A + B + C
    }

    // ══════════════════════════════════════════════════════════════
    //  Manager scope (AC-5/BR-1/BR-3)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Manager_overview_is_scoped_to_direct_reports_only()
    {
        var d = (await CreateService(ManagerUser()).GetOverviewAsync(Filter(_cycleId))).Value!;

        d.Scope.Should().Be("Team");
        d.ScoredEmployeeCount.Should().Be(2);          // A + B only — NOT Sales' C
        d.AverageScore.Should().Be(3.0m);              // (4 + 2) / 2
        d.TopPerformers.Should().NotContain(p => p.EmployeeId == _empC);
    }

    [Fact]
    public async Task Manager_gets_team_ranking_and_no_bottom_list()
    {
        var d = (await CreateService(ManagerUser()).GetOverviewAsync(Filter(_cycleId))).Value!;

        // BR-3: managers see a team ranking (top list) but no org-wide bottom list.
        d.TopPerformers.Should().HaveCount(2);
        d.TopPerformers.First().EmployeeId.Should().Be(_empA);  // 4.0 first
        d.BottomPerformers.Should().BeEmpty();
    }

    [Fact]
    public async Task Manager_cannot_drill_into_a_non_report_department()
    {
        // Manager drills into Sales (where they have no reports) — sees an empty population, never C's score.
        var d = (await CreateService(ManagerUser())
            .GetDepartmentDrilldownAsync(_deptSales, Filter(_cycleId))).Value!;

        d.Employees.Should().BeEmpty();
        d.Headcount.Should().Be(0);
    }

    [Fact]
    public async Task Employee_without_dashboard_permission_is_forbidden()
    {
        var result = await CreateService(EmployeeUser()).GetOverviewAsync(Filter(_cycleId));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("forbidden");
    }

    // ══════════════════════════════════════════════════════════════
    //  Department drill-down (FR-5)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Drilldown_lists_department_employees_with_scores()
    {
        var d = (await CreateService(HrUser())
            .GetDepartmentDrilldownAsync(_deptEng, Filter(_cycleId))).Value!;

        d.DepartmentName.Should().Be("Engineering");
        d.Employees.Should().HaveCount(2);
        d.Employees.Select(e => e.Score).Should().Contain(new decimal?[] { 4.0m, 2.0m });
        d.Employees.First().Score.Should().Be(4.0m);   // ordered desc by score
        d.AverageScore.Should().Be(3.0m);
    }

    // ══════════════════════════════════════════════════════════════
    //  Trend (AC-3/FR-7)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Trend_returns_a_point_per_cycle_with_average()
    {
        var result = await CreateService(HrUser())
            .GetTrendAsync(new[] { _cycleId }, Filter(), includeDepartmentSeries: true);

        result.IsSuccess.Should().BeTrue(result.Error);
        var t = result.Value!;
        t.Points.Should().ContainSingle();
        t.Points[0].AverageScore.Should().Be(3.67m);
        t.Points[0].ScoredEmployeeCount.Should().Be(3);
        t.DepartmentSeries.Should().HaveCount(2);   // Engineering + Sales overlays
    }

    // ══════════════════════════════════════════════════════════════
    //  Export (FR-8/AC-4)
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task Export_produces_a_non_empty_file(string format)
    {
        var result = await CreateService(HrUser()).ExportOverviewAsync(Filter(_cycleId), format);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.FileContent.Should().NotBeEmpty();
        result.Value!.FileName.Should().Contain("performance-dashboard");
    }

    [Fact]
    public async Task Export_rejects_unknown_format()
    {
        var result = await CreateService(HrUser()).ExportOverviewAsync(Filter(_cycleId), "png");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_format");
    }

    // ── DF-6/ISSUE-126: the branded dashboard PDF embeds the tenant logo (read in-process via IFileStorage). ──

    private PerformanceDashboardService CreateServiceWith(ICurrentUser user, IFileStorage fileStorage) => new(
        CreateDbContext(), _tenantContext, user, fileStorage,
        Substitute.For<ILogger<PerformanceDashboardService>>());

    // A minimal valid 1×1 PNG (decodable by QuestPDF's Image.FromBinaryData).
    private static byte[] ValidPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    [Trait("TC", "TC-PRF-007-16")]
    public async Task Export_pdf_WithTenantLogo_EmbedsLogo_LargerThanNoLogoBaseline()
    {
        // Baseline: no logo configured (LogoUrl null) → colour-band-only header.
        var noLogo = await CreateService(HrUser()).ExportOverviewAsync(Filter(_cycleId), "pdf");
        noLogo.IsSuccess.Should().BeTrue(noLogo.Error);
        System.Text.Encoding.ASCII.GetString(noLogo.Value!.FileContent, 0, 4).Should().Be("%PDF");

        // With a tenant logo: the in-process IFileStorage read returns a valid PNG → the image is embedded.
        _tenantContext.LogoUrl.Returns($"/{_tenantId}/branding/logo.png");
        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream(ValidPng())));

        var withLogo = await CreateServiceWith(HrUser(), storage).ExportOverviewAsync(Filter(_cycleId), "pdf");
        withLogo.IsSuccess.Should().BeTrue(withLogo.Error);
        System.Text.Encoding.ASCII.GetString(withLogo.Value!.FileContent, 0, 4).Should().Be("%PDF");

        await storage.Received().OpenReadAsync(_tenantId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        // The embedded logo image adds bytes — a dropped `.Image(logoBytes)` mutant makes both PDFs identical.
        withLogo.Value!.FileContent.Length.Should().BeGreaterThan(noLogo.Value!.FileContent.Length);
    }

    [Fact]
    [Trait("TC", "TC-PRF-007-16")]
    public async Task Export_pdf_NoTenantLogo_StillRenders_NoStorageRead()
    {
        _tenantContext.LogoUrl.Returns((string?)null);
        var storage = Substitute.For<IFileStorage>();

        var result = await CreateServiceWith(HrUser(), storage).ExportOverviewAsync(Filter(_cycleId), "pdf");

        result.IsSuccess.Should().BeTrue(result.Error);
        System.Text.Encoding.ASCII.GetString(result.Value!.FileContent, 0, 4).Should().Be("%PDF");
        await storage.DidNotReceive().OpenReadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    [Trait("TC", "TC-PRF-007-16")]
    public async Task Export_pdf_CorruptLogoBytes_DegradesGracefully_StillRendersPdf()
    {
        // A stored "logo" that is not a decodable image → IsRenderableImage rejects it → colour-only header, no crash.
        _tenantContext.LogoUrl.Returns($"/{_tenantId}/branding/logo.png");
        var storage = Substitute.For<IFileStorage>();
        storage.OpenReadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream?>(new MemoryStream(new byte[] { 1, 2, 3, 4, 5 })));

        var result = await CreateServiceWith(HrUser(), storage).ExportOverviewAsync(Filter(_cycleId), "pdf");

        result.IsSuccess.Should().BeTrue(result.Error);
        System.Text.Encoding.ASCII.GetString(result.Value!.FileContent, 0, 4).Should().Be("%PDF");
    }
}
