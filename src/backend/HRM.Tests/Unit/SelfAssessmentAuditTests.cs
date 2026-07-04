// ============================================================================
// ISSUE-106 (US-PRF-004) — Self-assessment save/submit MUST write an audit_logs row.
//
// Regression tests for the "missing-audit-write" cluster. SelfAssessmentService.UpsertAsync
// (reached via SaveDraftAsync / SubmitAsync) persists the assessment but writes NO queryable
// audit_logs row. The fix adds a DISTINCT action per operation — "SelfAssessment.Saved" for a
// draft save and "SelfAssessment.Submitted" for a submit (LeaveTypeService.AddLeaveTypeAudit
// pattern): tenant + actor from context, ResourceId == the assessment.
//
// Drives the REAL SelfAssessmentService through the InMemory harness (mirrors
// SelfAssessmentServiceTests). Includes the save-vs-submit distinction the cluster calls for.
// FAILS pre-fix (no rows) / PASSES post-fix.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
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

public sealed class SelfAssessmentAuditTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _goal1 = Guid.NewGuid();  // weight 60
    private readonly Guid _goal2 = Guid.NewGuid();  // weight 40

    private const string ValidComment = "Delivered everything on time and to spec.";

    public SelfAssessmentAuditTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.UserId.Returns(_userId);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.Permissions.Returns(new[] { "Performance.Read.Self" });
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private SelfAssessmentService Service() => new(
        Db(),
        _tenantContext,
        _currentUser,
        Substitute.For<IPerformanceNotificationService>(),
        Substitute.For<ILogger<SelfAssessmentService>>());

    private void Seed()
    {
        using var db = Db();
        var now = DateTime.UtcNow;

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.Employees.Add(new Employee
        {
            Id = _employeeId, TenantId = _tenantId, UserId = _userId,
            EmployeeNo = "EMP-0001", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
        });
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026",
            Status = AppraisalCycleStatus.Active,
            GoalSettingStart = now.AddDays(-60), GoalSettingEnd = now.AddDays(-30),
            SelfAssessmentStart = now.AddDays(-5), SelfAssessmentEnd = now.AddDays(5), // window OPEN
            RatingScaleMax = 5, SelfWeightPercent = 30, IsDeleted = false,
        });
        db.Goals.AddRange(Goal(_goal1, "Ship the API", 60), Goal(_goal2, "Mentor juniors", 40));
        db.SaveChanges();
    }

    private Goal Goal(Guid id, string title, int weight) => new()
    {
        Id = id, TenantId = _tenantId, CycleId = _cycleId, EmployeeId = _employeeId,
        Title = title, Category = GoalCategory.Kpi, Weight = weight, TargetValue = "100%",
        MeasurementUnit = "%", DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
        Status = GoalStatus.Acknowledged, IsDeleted = false,
    };

    private static SelfAssessmentItemInput Rated(Guid goalId, int rating, int pct, string comment)
        => new(goalId, rating, pct, comment);

    private async Task<Guid> AssessmentId()
    {
        using var db = Db();
        var a = await db.SelfAssessments.AsNoTracking()
            .SingleAsync(s => s.EmployeeId == _employeeId && s.CycleId == _cycleId);
        return a.Id;
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
    public async Task SaveDraft_WritesSavedAuditRow_ISSUE106()
    {
        Seed();
        var input = new SaveSelfAssessmentInput(_cycleId, new[] { Rated(_goal1, 4, 90, "partial progress") });

        var result = await Service().SaveDraftAsync(input);
        result.IsSuccess.Should().BeTrue();

        var assessmentId = await AssessmentId();
        var audits = await AuditsFor(assessmentId);

        audits.Should().Contain(a => ActionContains(a, "Saved"),
            "a draft save must write a SelfAssessment.Saved audit row (ISSUE-106)");
        var row = audits.Single(a => ActionContains(a, "Saved"));
        ActionContains(row, "SelfAssessment").Should().BeTrue();
        row.ResourceId.Should().Be(assessmentId.ToString());
        row.TenantId.Should().Be(_tenantId);
        row.UserId.Should().Be(_userId);
    }

    // ── Submit writes a DISTINCT "Submitted" audit row ────────────────

    [Fact]
    public async Task Submit_WritesSubmittedAuditRow_DistinctFromSaved_ISSUE106()
    {
        Seed();

        // First a draft save, then a full submit — both against the same assessment.
        (await Service().SaveDraftAsync(
            new SaveSelfAssessmentInput(_cycleId, new[] { Rated(_goal1, 4, 90, ValidComment) })))
            .IsSuccess.Should().BeTrue();

        var submit = new SaveSelfAssessmentInput(_cycleId, new[]
        {
            Rated(_goal1, 5, 100, ValidComment),
            Rated(_goal2, 3, 70, ValidComment),
        });
        (await Service().SubmitAsync(submit)).IsSuccess.Should().BeTrue();

        var assessmentId = await AssessmentId();
        var audits = await AuditsFor(assessmentId);

        // The submit action must be recorded DISTINCTLY from the draft-save action.
        audits.Should().Contain(a => ActionContains(a, "Saved"), "the earlier draft save is audited");
        audits.Should().Contain(a => ActionContains(a, "Submitted"),
            "a submit must write a distinct SelfAssessment.Submitted audit row (ISSUE-106)");

        var submitted = audits.Single(a => ActionContains(a, "Submitted"));
        submitted.ResourceId.Should().Be(assessmentId.ToString());
        submitted.TenantId.Should().Be(_tenantId);
        submitted.UserId.Should().Be(_userId);
        // Save and Submit are genuinely different actions, not the same string reused.
        ActionContains(submitted, "Saved").Should().BeFalse();
    }
}
