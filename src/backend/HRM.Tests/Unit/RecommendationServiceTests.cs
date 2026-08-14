// ============================================================================
// US-PRF-010: Performance-based recommendations — service unit tests.
//
// Drives RecommendationService through the real EF Core InMemory provider (so the recommendation,
// recommendation_approver, recommendation_event, recommendation_budget and recommendation_rule rows are actually
// persisted). Covers:
//   - AC-2/FR-2/BR-3: auto-generation applies the rating-threshold rules — correct employees flagged per threshold,
//     suggestions land in Draft, the highest matching rule wins, already-recommended employees are skipped.
//   - FR-3: a manual override REQUIRES a justification.
//   - BR-5: a promotion requires a target grade + effective date.
//   - AC-3/FR-4/BR-1/BR-2: submit gates — final ratings published (cycle Completed) + calibration complete; the
//     approval workflow (submit→pending→approved / submit→pending→rejected).
//   - FR-8/BR-4: budget soft-warning — over-allocation is allowed (never a hard block) and tracked.
//   - AC-4: summary stats — promotions, bonus pool, increment-by-department.
//   - AC-5/NFR-5: manager scope — a manager sees ONLY their direct reports; a general employee is forbidden.
//   - BR-6: the downstream-integration seam is raised on final approval (IntegrationRaised event + integration call).
//   - NFR-2: an unresolved tenant context is refused.
// ============================================================================

using System.Text.Json;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RecommendationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly IRecommendationIntegrationService _integration = Substitute.For<IRecommendationIntegrationService>();

    private readonly Guid _hrUserId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _otherUserId = Guid.NewGuid();
    private readonly Guid _approverUserId = Guid.NewGuid();

    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _approverEmpId = Guid.NewGuid();
    private readonly Guid _topPerformerId = Guid.NewGuid();   // reports to manager; final score 4.6
    private readonly Guid _midPerformerId = Guid.NewGuid();   // reports to manager; final score 3.6
    private readonly Guid _lowPerformerId = Guid.NewGuid();   // reports to manager; final score 2.0
    private readonly Guid _otherTeamEmpId = Guid.NewGuid();   // NOT a direct report
    private readonly Guid _otherEmpId = Guid.NewGuid();       // the unrelated employee user

    private readonly Guid _cycleId = Guid.NewGuid();
    private readonly Guid _deptId = Guid.NewGuid();

    public RecommendationServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private RecommendationService Service(ICurrentUser user)
    {
        var db = Db();
        return new(db, _tenantContext, user, _integration, Auditor(db, user),
            Substitute.For<ILogger<RecommendationService>>());
    }

    // Real audit writer over the SAME (name-shared InMemory) store so BUG-083 read-audit rows actually persist.
    private IPayrollAuditLogger Auditor(AppDbContext db, ICurrentUser user)
        => new HRM.Infrastructure.Services.PayrollAuditLogger(
            db, _tenantContext, user, Substitute.For<ILogger<HRM.Infrastructure.Services.PayrollAuditLogger>>());

    private ICurrentUser User(Guid userId, params string[] permissions)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("user@t.com");
        u.Permissions.Returns(permissions);
        return u;
    }

    // GAP-012 / ISSUE-373: the reveal path (GetAsync) now requires Payroll.ViewCompensation on top of
    // view-authorization — "may you see this recommendation" and "may you see their salary" are different
    // questions. HR here represents an HR MANAGER, who holds it in the built-in bundle. A caller without it is
    // covered by TheRevealPath_RefusesACallerWithoutTheCompensationPermission below.
    private ICurrentUser HrUser() =>
        User(_hrUserId, PermissionCatalog.Performance.PublishAll, PermissionCatalog.Payroll.ViewCompensation);
    private ICurrentUser ManagerUser() =>
        User(_managerUserId, PermissionCatalog.Performance.ReviewTeam, PermissionCatalog.Payroll.ViewCompensation);

    /// <summary>A caller authorized to VIEW the recommendation but not to see compensation figures.</summary>
    private ICurrentUser ManagerWithoutCompensation() =>
        User(_managerUserId, PermissionCatalog.Performance.ReviewTeam);
    private ICurrentUser OtherUser() => User(_otherUserId, PermissionCatalog.Performance.ReadSelf);
    private ICurrentUser ApproverUser() => User(_approverUserId, PermissionCatalog.Performance.ReviewTeam);

    private async Task SeedAsync(AppraisalCycleStatus cycleStatus = AppraisalCycleStatus.Completed, bool calibration = false)
    {
        using var db = Db();
        db.Departments.Add(new Department { Id = _deptId, TenantId = _tenantId, Name = "Engineering", Code = "ENG" });

        db.Employees.Add(new Employee { Id = _managerEmpId, TenantId = _tenantId, UserId = _managerUserId, EmployeeNo = "MGR", FirstName = "Grace", LastName = "Hopper", Email = "g@t.com", Status = EmployeeStatus.Active, DepartmentId = _deptId, DateOfJoining = new DateTime(2015, 1, 1) });
        db.Employees.Add(new Employee { Id = _approverEmpId, TenantId = _tenantId, UserId = _approverUserId, EmployeeNo = "APP", FirstName = "Vint", LastName = "Cerf", Email = "v@t.com", Status = EmployeeStatus.Active, DepartmentId = _deptId, DateOfJoining = new DateTime(2010, 1, 1) });
        db.Employees.Add(new Employee { Id = _topPerformerId, TenantId = _tenantId, EmployeeNo = "TOP", FirstName = "Ada", LastName = "Lovelace", Email = "a@t.com", Status = EmployeeStatus.Active, DepartmentId = _deptId, ReportsToEmployeeId = _managerEmpId, DateOfJoining = new DateTime(2021, 1, 1) });
        db.Employees.Add(new Employee { Id = _midPerformerId, TenantId = _tenantId, EmployeeNo = "MID", FirstName = "Alan", LastName = "Turing", Email = "t@t.com", Status = EmployeeStatus.Active, DepartmentId = _deptId, ReportsToEmployeeId = _managerEmpId, DateOfJoining = new DateTime(2020, 6, 1) });
        db.Employees.Add(new Employee { Id = _lowPerformerId, TenantId = _tenantId, EmployeeNo = "LOW", FirstName = "Kurt", LastName = "Godel", Email = "k@t.com", Status = EmployeeStatus.Active, DepartmentId = _deptId, ReportsToEmployeeId = _managerEmpId, DateOfJoining = new DateTime(2022, 1, 1) });
        db.Employees.Add(new Employee { Id = _otherTeamEmpId, TenantId = _tenantId, EmployeeNo = "OTM", FirstName = "Edsger", LastName = "Dijkstra", Email = "e@t.com", Status = EmployeeStatus.Active, DepartmentId = _deptId, DateOfJoining = new DateTime(2019, 1, 1) });
        db.Employees.Add(new Employee { Id = _otherEmpId, TenantId = _tenantId, UserId = _otherUserId, EmployeeNo = "OTH", FirstName = "Claude", LastName = "Shannon", Email = "c@t.com", Status = EmployeeStatus.Active, DepartmentId = _deptId, DateOfJoining = new DateTime(2018, 1, 1) });

        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY2026", Status = cycleStatus,
            StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31),
            RatingScaleMax = 5, IsCalibrationEnabled = calibration,
        });

        // Submitted manager reviews with final scores + a Promotion flag on the top performer.
        AddReview(db, _topPerformerId, 4.6m, ReviewFlag.Promotion);
        AddReview(db, _midPerformerId, 3.6m, ReviewFlag.None);
        AddReview(db, _lowPerformerId, 2.0m, ReviewFlag.None);
        AddReview(db, _otherTeamEmpId, 4.7m, ReviewFlag.None);

        await db.SaveChangesAsync();
    }

    private void AddReview(AppDbContext db, Guid empId, decimal finalScore, ReviewFlag flag) =>
        db.ManagerReviews.Add(new ManagerReview
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, CycleId = _cycleId, EmployeeId = empId,
            Status = ManagerReviewStatus.Submitted, FinalScore = finalScore, Flag = flag,
            SubmittedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        });

    private async Task SeedRulesAsync()
    {
        using var db = Db();
        db.RecommendationRules.Add(new RecommendationRule { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Promotion", MinFinalScore = 4.5m, RecommendedType = RecommendationType.Promotion, IsActive = true });
        db.RecommendationRules.Add(new RecommendationRule { Id = Guid.NewGuid(), TenantId = _tenantId, Name = "Bonus", MinFinalScore = 3.5m, RecommendedType = RecommendationType.Bonus, DefaultBonusPercent = 10m, IsActive = true });
        await db.SaveChangesAsync();
    }

    private static RecommendationDetailsInput Details(
        string? targetGrade = null, DateOnly? effective = null, decimal? bonusAmount = null,
        decimal? incrementAmount = null, Guid? budgetId = null) =>
        new(targetGrade, null, effective, bonusAmount, null, incrementAmount, null, null, null, budgetId);

    // ── AC-2/FR-2/BR-3: auto-generation ─────────────────────────────────

    [Fact]
    public async Task AutoGenerate_flags_correct_employees_per_threshold_as_draft_suggestions()
    {
        await SeedAsync();
        await SeedRulesAsync();

        var result = await Service(HrUser()).AutoGenerateAsync(_cycleId);

        result.IsSuccess.Should().BeTrue();
        // 4 reviews evaluated; top→Promotion (≥4.5), other-team→Promotion (≥4.7), mid→Bonus (≥3.5), low (2.0) no rule.
        result.Value!.EmployeesEvaluated.Should().Be(4);
        result.Value.SuggestionsCreated.Should().Be(3);
        result.Value.Suggestions.Should().OnlyContain(s => s.Status == RecommendationStatus.Draft && s.IsAutoGenerated);

        result.Value.Suggestions.Single(s => s.EmployeeId == _topPerformerId).Type.Should().Be(RecommendationType.Promotion);
        result.Value.Suggestions.Single(s => s.EmployeeId == _midPerformerId).Type.Should().Be(RecommendationType.Bonus);
        result.Value.Suggestions.Should().NotContain(s => s.EmployeeId == _lowPerformerId);
        // Bonus rule seeded its default percentage.
        result.Value.Suggestions.Single(s => s.EmployeeId == _midPerformerId).BonusPercent.Should().Be(10m);
    }

    [Fact]
    public async Task AutoGenerate_skips_employees_that_already_have_a_recommendation()
    {
        await SeedAsync();
        await SeedRulesAsync();
        // First run creates 3.
        (await Service(HrUser()).AutoGenerateAsync(_cycleId)).Value!.SuggestionsCreated.Should().Be(3);
        // Second run creates 0 (all already have one) and skips them.
        var second = await Service(HrUser()).AutoGenerateAsync(_cycleId);
        second.Value!.SuggestionsCreated.Should().Be(0);
        second.Value.SuggestionsSkipped.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AutoGenerate_requires_active_rules()
    {
        await SeedAsync();
        var result = await Service(HrUser()).AutoGenerateAsync(_cycleId);
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("no_rules");
    }

    [Fact]
    public async Task AutoGenerate_by_non_hr_is_forbidden()
    {
        await SeedAsync();
        await SeedRulesAsync();
        var result = await Service(ManagerUser()).AutoGenerateAsync(_cycleId);
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    // ── FR-3: manual override requires justification ────────────────────

    [Fact]
    public async Task Manual_create_succeeds_without_justification_but_override_requires_one()
    {
        await SeedAsync();

        var create = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null));
        create.IsSuccess.Should().BeTrue();

        // Override without justification → 422.
        var noJust = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 7000m), null, null));
        noJust.IsFailure.Should().BeTrue();
        noJust.ErrorCode.Should().Be("justification_required");

        // Override WITH justification → ok.
        var withJust = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 7000m), "Exceeded targets", null));
        withJust.IsSuccess.Should().BeTrue();
        withJust.Value!.Justification.Should().Be("Exceeded targets");
    }

    // ── BR-5: promotion requires grade + effective date ─────────────────

    [Fact]
    public async Task Promotion_requires_target_grade_and_effective_date()
    {
        await SeedAsync();

        var missing = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Promotion, Details(), null, null));
        missing.IsFailure.Should().BeTrue();
        missing.ErrorCode.Should().Be("promotion_details_required");

        var ok = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Promotion,
            Details(targetGrade: "L6", effective: new DateOnly(2027, 1, 1)), null, null));
        ok.IsSuccess.Should().BeTrue();
        ok.Value!.TargetGrade.Should().Be("L6");
    }

    // ── AC-3/BR-1/BR-2: submit gates ────────────────────────────────────

    [Fact]
    public async Task Submit_is_blocked_until_final_ratings_published()
    {
        await SeedAsync(cycleStatus: AppraisalCycleStatus.Active); // not yet Completed.
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null))).Value!;

        var submit = await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, [], null));
        submit.IsFailure.Should().BeTrue();
        submit.ErrorCode.Should().Be("final_ratings_not_published");
    }

    [Fact]
    public async Task Submit_with_calibration_enabled_requires_a_submitted_review()
    {
        await SeedAsync(calibration: true);
        // The low performer DOES have a submitted review in seed, so calibration passes; create+submit for them.
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _lowPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 1000m), null, null))).Value!;
        var ok = await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, [], null));
        ok.IsSuccess.Should().BeTrue();
        ok.Value!.Status.Should().Be(RecommendationStatus.Submitted); // no approvers ⇒ terminal "submitted".
    }

    [Fact]
    public async Task Submit_with_no_approvers_sets_status_submitted()
    {
        await SeedAsync();
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null))).Value!;

        var submit = await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, [], null));
        submit.IsSuccess.Should().BeTrue();
        submit.Value!.Status.Should().Be(RecommendationStatus.Submitted);
        submit.Value.SubmittedAt.Should().NotBeNull();
        submit.Value.Events.Should().Contain(e => e.EventType == RecommendationEventType.Submitted);
    }

    // ── FR-4: approval workflow ─────────────────────────────────────────

    [Fact]
    public async Task Approval_workflow_submit_to_pending_to_approved_raises_integration_seam()
    {
        await SeedAsync();
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Promotion,
            Details(targetGrade: "L6", effective: new DateOnly(2027, 1, 1)), null, null))).Value!;

        var submitted = await Service(HrUser()).SubmitAsync(
            new SubmitRecommendationInput(rec.Id, new[] { _approverEmpId }, null));
        submitted.Value!.Status.Should().Be(RecommendationStatus.PendingApproval);

        var approved = await Service(ApproverUser()).DecideAsync(
            new DecideRecommendationInput(rec.Id, true, "Looks good", null));
        approved.IsSuccess.Should().BeTrue();
        approved.Value!.Status.Should().Be(RecommendationStatus.Approved);
        // BR-6: integration seam raised on final approval.
        approved.Value.Events.Should().Contain(e => e.EventType == RecommendationEventType.IntegrationRaised);
        await _integration.Received().RaiseAsync(
            rec.Id, _topPerformerId, _cycleId, RecommendationType.Promotion, "core-hr", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approval_workflow_reject_terminates_as_rejected()
    {
        await SeedAsync();
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null))).Value!;
        await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, new[] { _approverEmpId }, null));

        var rejected = await Service(ApproverUser()).DecideAsync(
            new DecideRecommendationInput(rec.Id, false, "Budget unavailable", null));
        rejected.IsSuccess.Should().BeTrue();
        rejected.Value!.Status.Should().Be(RecommendationStatus.Rejected);
        rejected.Value.Events.Should().Contain(e => e.EventType == RecommendationEventType.Rejected);
    }

    [Fact]
    public async Task A_non_current_approver_cannot_decide()
    {
        await SeedAsync();
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null))).Value!;
        await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, new[] { _approverEmpId }, null));

        // The manager (not in the approver chain, and not HR) cannot decide.
        var result = await Service(ManagerUser()).DecideAsync(new DecideRecommendationInput(rec.Id, true, null, null));
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    // ── FR-8/BR-4: budget soft warning ──────────────────────────────────

    [Fact]
    public async Task Budget_overrun_with_a_justification_is_a_soft_warning_not_a_hard_block()
    {
        await SeedAsync();
        var budget = (await Service(HrUser()).SaveBudgetAsync(
            new SaveBudgetInput(_cycleId, "Bonus pool", 1000m, "USD"))).Value!;

        // ISSUE-145: a justification is present, so proceeding OVER budget is allowed (soft warning, not a cap).
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 1500m, budgetId: budget.Id),
            "Exceptional impact — over-budget approved by CFO", null))).Value!;

        // Submitting a 1500 bonus against a 1000 budget SUCCEEDS (BR-4 soft warning, not a block).
        var submit = await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, [], null));
        submit.IsSuccess.Should().BeTrue();

        var after = (await Service(HrUser()).GetBudgetAsync(_cycleId)).Value!;
        after.ConsumedAmount.Should().Be(1500m);
        after.IsExceeded.Should().BeTrue();
        after.RemainingAmount.Should().Be(-500m);
    }

    [Fact]
    public async Task ISSUE145_over_budget_submit_without_a_justification_is_rejected_and_does_not_charge()
    {
        await SeedAsync();
        var budget = (await Service(HrUser()).SaveBudgetAsync(
            new SaveBudgetInput(_cycleId, "Bonus pool", 100000m, "USD"))).Value!;

        // A 90k bonus (within the 100k budget) submits fine and charges the budget.
        var within = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 90000m, budgetId: budget.Id), null, null))).Value!;
        (await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(within.Id, [], null))).IsSuccess.Should().BeTrue();

        // A further 20k bonus (no justification) would push consumed to 110k > 100k ⇒ BR-4 gate: 400, not charged.
        var over = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _midPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 20000m, budgetId: budget.Id), null, null))).Value!;
        var rejected = await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(over.Id, [], null));
        rejected.IsFailure.Should().BeTrue();
        rejected.StatusCode.Should().Be(400);
        rejected.ErrorCode.Should().Be("over_budget_requires_justification");

        // The rejected submit must NOT have consumed the budget — consumed stays at the 90k first charge.
        (await Service(HrUser()).GetBudgetAsync(_cycleId)).Value!.ConsumedAmount.Should().Be(90000m);
    }

    [Fact]
    public async Task ISSUE145_over_budget_submit_with_a_justification_succeeds_and_charges()
    {
        await SeedAsync();
        var budget = (await Service(HrUser()).SaveBudgetAsync(
            new SaveBudgetInput(_cycleId, "Bonus pool", 100000m, "USD"))).Value!;

        var within = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 90000m, budgetId: budget.Id), null, null))).Value!;
        (await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(within.Id, [], null))).IsSuccess.Should().BeTrue();

        // Same 20k over-budget bonus, but WITH a justification ⇒ soft warning, proceeds and charges to 110k.
        var over = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _midPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 20000m, budgetId: budget.Id),
            "Retention-critical hire", null))).Value!;
        (await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(over.Id, [], null))).IsSuccess.Should().BeTrue();

        var after = (await Service(HrUser()).GetBudgetAsync(_cycleId)).Value!;
        after.ConsumedAmount.Should().Be(110000m);
        after.IsExceeded.Should().BeTrue();
    }

    // ── AC-4: summary stats ─────────────────────────────────────────────

    [Fact]
    public async Task Summary_aggregates_promotions_bonus_pool_and_increment_by_department()
    {
        await SeedAsync();
        var promo = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Promotion,
            Details(targetGrade: "L6", effective: new DateOnly(2027, 1, 1)), null, null))).Value!;
        await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(promo.Id, [], null));

        var inc = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _midPerformerId, _cycleId, RecommendationType.Increment, Details(incrementAmount: 3000m), null, null))).Value!;
        await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(inc.Id, [], null));

        var bonus = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _lowPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 2000m), null, null))).Value!;
        await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(bonus.Id, [], null));

        var summary = await Service(HrUser()).GetSummaryAsync(_cycleId);
        summary.IsSuccess.Should().BeTrue();
        summary.Value!.TotalPromotions.Should().Be(1);
        summary.Value.TotalBonusPoolAllocated.Should().Be(2000m);
        summary.Value.TotalIncrementAllocated.Should().Be(3000m);
        summary.Value.IncrementByDepartment.Should().ContainSingle()
            .Which.TotalIncrementAmount.Should().Be(3000m);
    }

    // ── AC-5/NFR-5: manager scope ───────────────────────────────────────

    [Fact]
    public async Task Manager_workspace_only_includes_direct_reports()
    {
        await SeedAsync();
        var ws = await Service(ManagerUser()).GetWorkspaceAsync(new RecommendationWorkspaceQueryInput(_cycleId, 1, 100));
        ws.IsSuccess.Should().BeTrue();
        var ids = ws.Value!.Rows.Select(r => r.EmployeeId).ToList();
        ids.Should().Contain(new[] { _topPerformerId, _midPerformerId, _lowPerformerId });
        ids.Should().NotContain(_otherTeamEmpId); // not a direct report.
    }

    [Fact]
    public async Task General_employee_cannot_access_the_workspace()
    {
        await SeedAsync();
        var ws = await Service(OtherUser()).GetWorkspaceAsync(new RecommendationWorkspaceQueryInput(_cycleId, 1, 100));
        ws.IsFailure.Should().BeTrue();
        ws.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Manager_cannot_view_a_non_report_recommendation()
    {
        await SeedAsync();
        // HR creates a recommendation for the OTHER team employee (not the manager's report).
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _otherTeamEmpId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 1000m), null, null))).Value!;

        var result = await Service(ManagerUser()).GetAsync(rec.Id);
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
    }

    // ── BUG-083: GetAsync (which decrypts + returns compensation) emits a
    //    Recommendation.ViewSensitive read-audit naming the comp fields, NO values ──
    [Fact]
    public async Task TheWorkspaceFlag_ReflectsTheCompensationPermission_BothWays()
    {
        // GAP-012 / ISSUE-373. This arm exists because mutation testing caught its absence: hardcoding
        // CompensationVisible = true broke NOTHING, which is the same "field with no test binding it" pattern
        // that let the frontend read this flag for months while no permission existed behind it. A flag that is
        // not asserted in BOTH directions is indistinguishable from a constant.
        await SeedAsync();

        var permitted = await Service(HrUser()).GetWorkspaceAsync(new RecommendationWorkspaceQueryInput(_cycleId, 1, 100));
        permitted.Value!.CompensationVisible.Should().BeTrue(
            "HR Manager holds Payroll.ViewCompensation in the built-in bundle");

        var denied = await Service(ManagerWithoutCompensation()).GetWorkspaceAsync(new RecommendationWorkspaceQueryInput(_cycleId, 1, 100));
        denied.Value!.CompensationVisible.Should().BeFalse(
            "a caller without the permission must be told so, or the UI offers a reveal that will 403");
    }

    [Fact]
    public async Task TheRevealPath_RefusesACallerWithoutTheCompensationPermission()
    {
        // GAP-012 / ISSUE-373. GetAsync DECRYPTS compensation, so it needs its own gate: AuthorizeViewAsync
        // answers "may you see this recommendation" — a manager may, for their own report — which is a
        // different question from "may you see their salary". This caller passes the first and must fail the
        // second.
        await SeedAsync();
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 4321m), null, null))).Value!;

        var result = await Service(ManagerWithoutCompensation()).GetAsync(rec.Id);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("compensation_not_permitted");
    }

    [Fact]
    public async Task TheRevealPath_LogsNoAuditRow_WhenCompensationIsRefused()
    {
        // The refusal happens BEFORE the read and before the audit write, so a rejected attempt cannot leave a
        // "compensation was viewed" row behind. An audit trail that records reveals that never happened is
        // worse than none — it would make a real incident unreadable.
        await SeedAsync();
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 4321m), null, null))).Value!;

        await Service(ManagerWithoutCompensation()).GetAsync(rec.Id);

        using var db = Db();
        var audits = await db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.Action == PayrollAuditAction.RecommendationViewSensitive)
            .ToListAsync();
        audits.Should().BeEmpty("a refused reveal must not be audited as a reveal");
    }

    [Fact]
    public async Task BUG083_GetAsync_emits_ViewSensitive_read_audit_without_compensation_values()
    {
        await SeedAsync();
        // HR creates a bonus recommendation carrying a distinctive amount.
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 4321m), null, null))).Value!;

        var result = await Service(HrUser()).GetAsync(rec.Id);
        result.IsSuccess.Should().BeTrue();
        result.Value!.BonusAmount.Should().Be(4321m); // the reveal genuinely returns the decrypted value.

        using var read = Db();
        var row = await read.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.Action == PayrollAuditAction.RecommendationViewSensitive);

        row.ResourceType.Should().Be("Recommendation");
        row.ResourceId.Should().Be(rec.Id.ToString());
        row.UserId.Should().Be(_hrUserId);
        row.TenantId.Should().Be(_tenantId);
        row.Before.Should().BeNull();

        var map = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.After!)!;
        map.Keys.Should().BeEquivalentTo(new[] { "fields", "recommendationId", "employeeId" });
        map["fields"].EnumerateArray().Select(e => e.GetString())
            .Should().Contain(new[] { "currentCompensation", "bonusAmount", "incrementAmount" });

        // The revealed compensation VALUE must NEVER be persisted in the audit row.
        row.After!.Should().NotContain("4321");
    }

    // ── BUG-083: a DENIED (403) read must emit NO ViewSensitive audit row —
    //    the read-audit fires only AFTER successful authorization (symmetry with the payslip path) ──
    [Fact]
    public async Task BUG083_UnauthorizedGetAsync_writes_no_ViewSensitive_read_audit()
    {
        await SeedAsync();
        // HR creates a recommendation for an employee who is NOT the manager's direct report.
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _otherTeamEmpId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 4321m), null, null))).Value!;

        // A manager who does not manage this employee is denied — the read never succeeds.
        var result = await Service(ManagerUser()).GetAsync(rec.Id);
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);

        // A denied read must leave no sensitive-read audit trail (would fail if the audit
        // call were moved ABOVE the authorization check in GetAsync).
        using var read = Db();
        (await read.AuditLogs.IgnoreQueryFilters()
            .AnyAsync(a => a.Action == PayrollAuditAction.RecommendationViewSensitive))
            .Should().BeFalse();
    }

    // ── NFR-2: tenant scoping ───────────────────────────────────────────

    [Fact]
    public async Task Unresolved_tenant_is_refused()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var svc = new RecommendationService(
            TestDbContextFactory.Create(ctx, _dbName), ctx, HrUser(), _integration,
            Substitute.For<IPayrollAuditLogger>(),
            Substitute.For<ILogger<RecommendationService>>());

        var result = await svc.AutoGenerateAsync(_cycleId);
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }
}
