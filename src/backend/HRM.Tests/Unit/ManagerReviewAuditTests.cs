// ============================================================================
// ISSUE-111 (US-PRF-005) — Manager-review save/submit MUST write an audit_logs row.
//
// Regression tests for the "missing-audit-write" cluster. ManagerReviewService.UpsertAsync
// (reached via SaveDraftAsync / SubmitAsync) persists the review but writes NO queryable
// audit_logs row (FR-7 relied only on the AuditInterceptor + Serilog). The fix adds a DISTINCT
// action per operation — "ManagerReview.Saved" for a draft save and "ManagerReview.Submitted"
// for a submit (LeaveTypeService.AddLeaveTypeAudit pattern): tenant + actor from context,
// ResourceId == the review.
//
// Drives the REAL ManagerReviewService through the InMemory harness (mirrors
// ManagerReviewServiceTests). The acting manager is authenticated and manages the reviewee, so
// the audit actor is non-null. Includes the save-vs-submit distinction. FAILS pre-fix / PASSES post-fix.
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ManagerReviewAuditTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _managerUser;

    private readonly Guid _managerEmployeeId = Guid.NewGuid();
    private readonly Guid _reportEmployeeId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _goal1 = Guid.NewGuid(); // weight 60
    private readonly Guid _goal2 = Guid.NewGuid(); // weight 40

    private const string ValidComment = "Strong delivery; consistently exceeded the agreed targets.";

    public ManagerReviewAuditTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _managerUser = Substitute.For<ICurrentUser>();
        _managerUser.UserId.Returns(_managerUserId);
        _managerUser.IsAuthenticated.Returns(true);
        _managerUser.Email.Returns("grace@acme.com");
        _managerUser.Permissions.Returns(new[] { PermissionCatalog.Performance.ReviewTeam });
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private ManagerReviewService Service() => new(
        Db(),
        _tenantContext,
        _managerUser,
        Substitute.For<IPerformanceNotificationService>(),
        Substitute.For<ILogger<ManagerReviewService>>());

    private void Seed()
    {
        using var db = Db();
        var now = DateTime.UtcNow;

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.Employees.Add(new Employee
        {
            Id = _managerEmployeeId, TenantId = _tenantId, UserId = _managerUserId,
            EmployeeNo = "EMP-MGR", FirstName = "Grace", LastName = "Hopper",
            Email = "grace@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
        });
        db.Employees.Add(new Employee
        {
            Id = _reportEmployeeId, TenantId = _tenantId,
            EmployeeNo = "EMP-RPT", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", Status = EmployeeStatus.Active,
            ReportsToEmployeeId = _managerEmployeeId, IsDeleted = false,
        });
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026",
            Status = AppraisalCycleStatus.Active,
            GoalSettingStart = now.AddDays(-90), GoalSettingEnd = now.AddDays(-60),
            SelfAssessmentStart = now.AddDays(-30), SelfAssessmentEnd = now.AddDays(-10),
            ManagerReviewStart = now.AddDays(-5), ManagerReviewEnd = now.AddDays(5), // window OPEN
            RatingScaleMax = 5, SelfWeightPercent = 30, IsDeleted = false,
        });
        db.Goals.AddRange(Goal(_goal1, "Ship the API", 60), Goal(_goal2, "Mentor juniors", 40));
        db.SaveChanges();
    }

    private Goal Goal(Guid id, string title, int weight) => new()
    {
        Id = id, TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _reportEmployeeId,
        Title = title, Category = GoalCategory.Kpi, Weight = weight, TargetValue = "100%",
        MeasurementUnit = "%", DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        Status = GoalStatus.Acknowledged, IsDeleted = false,
    };

    private static ManagerReviewItemInput Rated(Guid goalId, int rating, string comment)
        => new(goalId, rating, comment);

    private SaveManagerReviewInput Input(params ManagerReviewItemInput[] items)
        => new(_cycleId, _reportEmployeeId, "Overall a very strong year.", ReviewFlag.None, items);

    private async Task<Guid> ReviewId()
    {
        using var db = Db();
        var r = await db.ManagerReviews.AsNoTracking()
            .SingleAsync(x => x.EmployeeId == _reportEmployeeId && x.CycleId == _cycleId);
        return r.Id;
    }

    private async Task<List<AuditLog>> AuditsFor(Guid resourceId)
    {
        using var db = Db();
        return await db.AuditLogs.AsNoTracking()
            .Where(a => a.ResourceId == resourceId.ToString())
            .ToListAsync();
    }

    private static bool ActionContains(AuditLog a, string s)
        => (a.Action?.Contains(s) ?? false) || (a.EventType?.Contains(s) ?? false);

    // ── Draft save writes a "Saved" audit row ─────────────────────────

    [Fact]
    public async Task SaveDraft_WritesSavedAuditRow_ISSUE111()
    {
        Seed();

        var result = await Service().SaveDraftAsync(Input(Rated(_goal1, 4, "partial draft note here")));
        result.IsSuccess.Should().BeTrue();

        var reviewId = await ReviewId();
        var audits = await AuditsFor(reviewId);

        audits.Should().Contain(a => ActionContains(a, "Saved"),
            "a draft save must write a ManagerReview.Saved audit row (ISSUE-111)");
        var row = audits.Single(a => ActionContains(a, "Saved"));
        ActionContains(row, "ManagerReview").Should().BeTrue();
        row.ResourceId.Should().Be(reviewId.ToString());
        row.TenantId.Should().Be(_tenantId);
        row.UserId.Should().Be(_managerUserId);
    }

    // ── Submit writes a DISTINCT "Submitted" audit row ────────────────

    [Fact]
    public async Task Submit_WritesSubmittedAuditRow_DistinctFromSaved_ISSUE111()
    {
        Seed();

        (await Service().SaveDraftAsync(Input(Rated(_goal1, 4, ValidComment))))
            .IsSuccess.Should().BeTrue();

        (await Service().SubmitAsync(Input(Rated(_goal1, 5, ValidComment), Rated(_goal2, 3, ValidComment))))
            .IsSuccess.Should().BeTrue();

        var reviewId = await ReviewId();
        var audits = await AuditsFor(reviewId);

        audits.Should().Contain(a => ActionContains(a, "Saved"), "the earlier draft save is audited");
        audits.Should().Contain(a => ActionContains(a, "Submitted"),
            "a submit must write a distinct ManagerReview.Submitted audit row (ISSUE-111)");

        var submitted = audits.Single(a => ActionContains(a, "Submitted"));
        submitted.ResourceId.Should().Be(reviewId.ToString());
        submitted.TenantId.Should().Be(_tenantId);
        submitted.UserId.Should().Be(_managerUserId);
        ActionContains(submitted, "Saved").Should().BeFalse();
    }
}
