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
        return new(db, _tenantContext, user,
            new GanssHtmlSanitizer(), // ISSUE-149(b): the REAL sanitizer, so the write-path arms exercise it for real
            _integration, Auditor(db, user),
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

        // F3 / GAP-021: BR-2 now reads a real phase-completion fact, so a calibration-enabled cycle needs a
        // Calibration PHASE to exist. Seeded OPEN (CompletedOn = null) — the arms below complete it
        // explicitly, which is the only way to reach the permitted state.
        if (calibration)
        {
            db.CyclePhases.Add(new CyclePhase
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, CycleId = _cycleId,
                PhaseType = CyclePhaseType.Calibration, Sequence = 3,
                StartDate = new DateTime(2026, 10, 1), EndDate = new DateTime(2026, 11, 30),
            });
        }

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

    // ── BR-2 ────────────────────────────────────────────────────────────────────────────────
    //
    // REWRITTEN for F3 / GAP-021, and the rewrite is the point. This test used to be named
    // `Submit_with_calibration_enabled_requires_a_submitted_review` and asserted that a SUBMITTED MANAGER
    // REVIEW opened the gate — its own comment read "The low performer DOES have a submitted review in
    // seed, so calibration passes."
    //
    // That was the defect, not the specification. BR-2's error code is `calibration_incomplete`, and the
    // code it guarded never looked at calibration at all: the gate passed with ZERO calibrations applied,
    // and this green test was the evidence of it. The gap analysis cites this very test as proof.
    //
    // So the assertion changed because the BEHAVIOUR it pinned was wrong — not to make a red test green.
    // It is strictly stronger now: the old version could not distinguish a calibrated cycle from an
    // uncalibrated one, and these two arms fail if the gate stops reading the phase in either direction.
    //
    // Note it reads the PHASE, never "does this employee have a RatingCalibration row". The normal
    // committee outcome for most employees is NO adjustment, so per-employee evidence is permanently
    // absent for them — a per-employee gate would never open for the majority.

    [Fact]
    public async Task Submit_with_calibration_enabled_is_BLOCKED_until_the_phase_is_complete_BR2()
    {
        await SeedAsync(calibration: true);
        // The low performer has a submitted manager review in seed. Under the OLD proxy that alone opened
        // the gate; it must not now.
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _lowPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 1000m), null, null))).Value!;

        var blocked = await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, [], null));

        blocked.IsFailure.Should().BeTrue(
            "a submitted manager review is not calibration; the old gate passed with zero calibrations applied");
        blocked.StatusCode.Should().Be(422);
        blocked.ErrorCode.Should().Be("calibration_incomplete");
    }

    [Fact]
    public async Task Submit_with_calibration_enabled_succeeds_once_the_phase_is_marked_complete_BR2()
    {
        await SeedAsync(calibration: true);
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _lowPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 1000m), null, null))).Value!;

        // Complete the Calibration phase — the fact BR-2 actually reads.
        using (var db = Db())
        {
            var phase = db.CyclePhases
                .Single(p => p.CycleId == _cycleId && p.PhaseType == CyclePhaseType.Calibration);
            phase.CompletedOn = DateTime.UtcNow;
            db.SaveChanges();
        }

        var ok = await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, [], null));

        ok.IsSuccess.Should().BeTrue(ok.Error);
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

    // ════════════════════════════════════════════════════════════════════
    //  ISSUE-149(a): the WRITE paths must leave a central audit trail.
    //
    //  Before this, RecommendationService held exactly ONE audit call — on a sensitive READ. A compensation
    //  recommendation could be created, overridden, submitted and approved with nothing in audit_log, while
    //  merely LOOKING at one was recorded. (AuditInterceptor is no fallback: it only stamps CreatedAt /
    //  CreatedBy, it never adds AuditLogs rows.)
    //
    //  Each arm asserts the ACTION NAME and the RESOURCE ID, not "some row exists" — an audit trail you
    //  cannot query by action is not an audit trail.
    // ════════════════════════════════════════════════════════════════════

    private async Task<List<AuditLog>> AuditRowsAsync(string action)
    {
        using var read = Db();
        return await read.AuditLogs.IgnoreQueryFilters().Where(a => a.Action == action).ToListAsync();
    }

    [Fact]
    public async Task ISSUE149a_Create_writes_a_RecommendationCreated_audit_row()
    {
        await SeedAsync();

        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 4321m), null, null))).Value!;

        var rows = await AuditRowsAsync(PayrollAuditAction.RecommendationCreated);
        var row = rows.Should().ContainSingle().Subject;

        row.ResourceType.Should().Be("Recommendation");
        row.ResourceId.Should().Be(rec.Id.ToString());
        row.UserId.Should().Be(_hrUserId);
        row.TenantId.Should().Be(_tenantId);
        row.Before.Should().BeNull("a create has no prior state");

        var after = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.After!)!;
        after["status"].GetString().Should().Be(nameof(RecommendationStatus.Draft));
        after["type"].GetString().Should().Be(nameof(RecommendationType.Bonus));
        after["isAutoGenerated"].GetBoolean().Should().BeFalse();
        after["compensationFields"].EnumerateArray().Select(e => e.GetString())
            .Should().Contain("bonusAmount");

        // Same posture as the ViewSensitive read-audit and as GetWorkspaceAsync (which nulls
        // CurrentCompensation): the audit NAMES the compensation fields, it never carries the figure.
        row.After!.Should().NotContain("4321");
    }

    [Fact]
    public async Task ISSUE149a_Override_writes_an_Overridden_audit_row_naming_the_changed_fields()
    {
        await SeedAsync();
        var hr = Service(HrUser());

        var rec = (await hr.SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 4321m), null, null))).Value!;

        // FR-3: overriding an existing recommendation requires a justification. This is the highest-value
        // audit row in the module — who moved the number, and why.
        var overridden = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 9876m),
            "Retention risk — matched a competing offer.", null));
        overridden.IsSuccess.Should().BeTrue(overridden.Error);

        var rows = await AuditRowsAsync(PayrollAuditAction.RecommendationOverridden);
        var row = rows.Should().ContainSingle().Subject;

        row.ResourceType.Should().Be("Recommendation");
        row.ResourceId.Should().Be(rec.Id.ToString());
        row.UserId.Should().Be(_hrUserId);
        row.Before.Should().NotBeNull("an override must record the state it replaced");

        var after = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.After!)!;
        var changed = after["changedFields"].EnumerateArray().Select(e => e.GetString()).ToList();
        changed.Should().Contain("bonusAmount", "the compensation figure moved and the audit must say WHICH field moved");
        changed.Should().Contain("justification");
        after["justification"].GetString().Should().Be("Retention risk — matched a competing offer.");

        // The before/after FIGURES are deliberately absent — bonus/increment columns are encrypted at rest and
        // gated behind Payroll.ViewCompensation, so republishing them into audit_log would defeat both.
        row.Before!.Should().NotContain("4321");
        row.After!.Should().NotContain("9876");
    }

    [Fact]
    public async Task ISSUE149a_Submit_writes_a_RecommendationSubmitted_audit_row()
    {
        await SeedAsync();
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null))).Value!;

        var submit = await Service(HrUser()).SubmitAsync(
            new SubmitRecommendationInput(rec.Id, new[] { _approverEmpId }, null));
        submit.IsSuccess.Should().BeTrue(submit.Error);

        var rows = await AuditRowsAsync(PayrollAuditAction.RecommendationSubmitted);
        var row = rows.Should().ContainSingle().Subject;

        row.ResourceType.Should().Be("Recommendation");
        row.ResourceId.Should().Be(rec.Id.ToString());
        row.UserId.Should().Be(_hrUserId);

        var before = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.Before!)!;
        before["status"].GetString().Should().Be(nameof(RecommendationStatus.Draft));

        var after = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.After!)!;
        after["status"].GetString().Should().Be(nameof(RecommendationStatus.PendingApproval));
        after["approverCount"].GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ISSUE149a_Approve_writes_a_RecommendationApproved_audit_row_attributed_to_the_approver()
    {
        await SeedAsync();
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null))).Value!;
        await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, new[] { _approverEmpId }, null));

        var approved = await Service(ApproverUser()).DecideAsync(
            new DecideRecommendationInput(rec.Id, true, "Approved by finance.", null));
        approved.IsSuccess.Should().BeTrue(approved.Error);

        var rows = await AuditRowsAsync(PayrollAuditAction.RecommendationApproved);
        var row = rows.Should().ContainSingle().Subject;

        row.ResourceType.Should().Be("Recommendation");
        row.ResourceId.Should().Be(rec.Id.ToString());
        row.UserId.Should().Be(_approverUserId, "the audit must attribute the decision to whoever actually decided");

        var after = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.After!)!;
        after["status"].GetString().Should().Be(nameof(RecommendationStatus.Approved));
        after["stepOrder"].GetInt32().Should().Be(0);
        after["isFinalDecision"].GetBoolean().Should().BeTrue();
        after["comment"].GetString().Should().Be("Approved by finance.");

        (await AuditRowsAsync(PayrollAuditAction.RecommendationRejected)).Should().BeEmpty();
    }

    [Fact]
    public async Task ISSUE149a_Reject_writes_a_RecommendationRejected_audit_row_not_an_Approved_one()
    {
        // The two decisions share one call site behind a ternary, so an arm that only ever exercises approve
        // would pass with the action hardcoded to Approved. This is the arm that pins the ternary.
        await SeedAsync();
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null))).Value!;
        await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, new[] { _approverEmpId }, null));

        var rejected = await Service(ApproverUser()).DecideAsync(
            new DecideRecommendationInput(rec.Id, false, "Budget frozen.", null));
        rejected.IsSuccess.Should().BeTrue(rejected.Error);

        var row = (await AuditRowsAsync(PayrollAuditAction.RecommendationRejected)).Should().ContainSingle().Subject;
        row.ResourceId.Should().Be(rec.Id.ToString());
        row.UserId.Should().Be(_approverUserId);

        var after = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(row.After!)!;
        after["status"].GetString().Should().Be(nameof(RecommendationStatus.Rejected));

        (await AuditRowsAsync(PayrollAuditAction.RecommendationApproved)).Should().BeEmpty();
    }

    [Fact]
    public async Task ISSUE149a_AutoGenerate_writes_a_Created_audit_row_per_generated_recommendation()
    {
        // Auto-generation is a bulk CREATE path — it produced real Draft recommendations with no central
        // trail at all. Same action as the manual create, distinguished by isAutoGenerated.
        await SeedAsync();
        await SeedRulesAsync();

        var generated = await Service(HrUser()).AutoGenerateAsync(_cycleId);
        generated.IsSuccess.Should().BeTrue(generated.Error);
        generated.Value!.SuggestionsCreated.Should().BeGreaterThan(0);

        var rows = await AuditRowsAsync(PayrollAuditAction.RecommendationCreated);
        rows.Should().HaveCount(generated.Value.SuggestionsCreated);
        rows.Select(r => r.ResourceId).Should().BeEquivalentTo(
            generated.Value.Suggestions.Select(s => s.Id.ToString()));

        var after = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rows[0].After!)!;
        after["isAutoGenerated"].GetBoolean().Should().BeTrue();
    }

    // ════════════════════════════════════════════════════════════════════
    //  ISSUE-149(b): sanitize free text on write
    //
    //  Four operator-entered free-text fields were persisted with a bare .Trim(). #666 (ISSUE-149a) widened
    //  the sink: `justification` and `customTypeLabel` are now copied VERBATIM into audit_log.before/after
    //  through AuditPayload, and the approval `comment` lands in the append-only FR-7 RecommendationEvent.
    //  Audit rows are immutable and retained, so sanitizing at write time can never clean what is already
    //  stored — the only fix is to stop writing new ones. There is no innerHTML sink rendering these fields
    //  today; this is defence-in-depth, not a live-XSS fix, and these arms are what keep it that way.
    // ════════════════════════════════════════════════════════════════════

    private const string Payload = "<script>alert(1)</script><img src=x onerror=alert(2)>";

    [Fact]
    public async Task ISSUE149b_Create_strips_dangerous_html_from_justification()
    {
        await SeedAsync();

        var create = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m),
            "Exceeded targets" + Payload, null));
        create.IsSuccess.Should().BeTrue(create.Error);

        create.Value!.Justification.Should().NotContain("<script").And.NotContain("alert(")
            .And.NotContain("onerror");
        create.Value.Justification.Should().Contain("Exceeded targets", "the HR user's real text must survive");

        // The whole point of ISSUE-149(b): the payload must not reach the immutable audit row either.
        var row = (await AuditRowsAsync(PayrollAuditAction.RecommendationCreated)).Should().ContainSingle().Subject;
        row.After!.Should().NotContain("<script").And.NotContain("onerror",
            "audit_log rows are immutable and retained — an unsanitized value written here is permanent");
    }

    [Fact]
    public async Task ISSUE149b_Override_strips_dangerous_html_from_justification()
    {
        await SeedAsync();
        await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null));

        var overridden = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 7000m),
            "Retention risk" + Payload, null));
        overridden.IsSuccess.Should().BeTrue(overridden.Error);

        overridden.Value!.Justification.Should().NotContain("<script").And.NotContain("alert(")
            .And.NotContain("onerror");
        overridden.Value.Justification.Should().Contain("Retention risk");

        var row = (await AuditRowsAsync(PayrollAuditAction.RecommendationOverridden)).Should().ContainSingle().Subject;
        row.After!.Should().NotContain("<script").And.NotContain("onerror");
    }

    /// <summary>
    /// ISSUE-121 ordering, applied to FR-3: a justification that is nothing but a payload sanitizes to an
    /// empty string. Checking "is a justification present?" on the RAW input would let it satisfy FR-3 and
    /// then store nothing — the override would be recorded as justified when it is not. Sanitize first.
    /// </summary>
    [Fact]
    public async Task ISSUE149b_Override_justification_that_is_only_a_payload_is_refused()
    {
        await SeedAsync();
        await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null));

        var overridden = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 7000m),
            "<script>alert(1)</script>", null));

        overridden.IsFailure.Should().BeTrue("markup with no text is not a justification");
        overridden.ErrorCode.Should().Be("justification_required");

        using var db = Db();
        var stored = await db.Recommendations.AsNoTracking().SingleAsync();
        stored.BonusAmount.Should().Be(5000m, "the refused override must not have mutated the recommendation");
    }

    /// <summary>
    /// The over-sanitization guard. HR writes prose with punctuation, percentages and currency; a fix that
    /// strips too much would corrupt the FR-3 record of WHY a compensation decision was made.
    /// </summary>
    [Fact]
    public async Task ISSUE149b_Preserves_benign_justification()
    {
        await SeedAsync();
        const string benign = "Top 10% performer - retention risk (competing offer, 2 rounds); approved by VP.";

        var create = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m),
            "  " + benign + "  ", null));
        create.IsSuccess.Should().BeTrue(create.Error);

        create.Value!.Justification.Should().Be(benign,
            "only surrounding whitespace is trimmed; legitimate punctuation round-trips byte-for-byte");
    }

    [Fact]
    public async Task ISSUE149b_Save_strips_dangerous_html_from_custom_type_label()
    {
        await SeedAsync();

        var create = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Custom,
            new RecommendationDetailsInput(null, null, null, null, null, null, null, null,
                "Sabbatical" + Payload, null),
            null, null));
        create.IsSuccess.Should().BeTrue(create.Error);

        create.Value!.CustomTypeLabel.Should().NotContain("<script").And.NotContain("alert(")
            .And.NotContain("onerror");
        create.Value.CustomTypeLabel.Should().Contain("Sabbatical");

        var row = (await AuditRowsAsync(PayrollAuditAction.RecommendationCreated)).Should().ContainSingle().Subject;
        row.After!.Should().NotContain("<script").And.NotContain("onerror");
    }

    [Fact]
    public async Task ISSUE149b_Preserves_benign_custom_type_label()
    {
        await SeedAsync();
        const string benign = "Sabbatical (3 months, unpaid) - 2027 intake";

        var create = await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Custom,
            new RecommendationDetailsInput(null, null, null, null, null, null, null, null, "  " + benign + "  ", null),
            null, null));
        create.IsSuccess.Should().BeTrue(create.Error);

        create.Value!.CustomTypeLabel.Should().Be(benign, "sanitizing must not rewrite a legitimate label");
    }

    [Fact]
    public async Task ISSUE149b_Decide_strips_dangerous_html_from_approval_comment()
    {
        await SeedAsync();
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null))).Value!;
        await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, new[] { _approverEmpId }, null));

        var rejected = await Service(ApproverUser()).DecideAsync(
            new DecideRecommendationInput(rec.Id, false, "Budget unavailable" + Payload, null));
        rejected.IsSuccess.Should().BeTrue(rejected.Error);

        using var db = Db();
        var comment = await db.RecommendationApprovers.AsNoTracking().Select(a => a.Comment).SingleAsync();
        comment.Should().NotBeNull();
        comment!.Should().NotContain("<script").And.NotContain("alert(").And.NotContain("onerror");
        comment.Should().Contain("Budget unavailable");

        // FR-7: the same text is echoed into the append-only event detail, which has no edit path either.
        var detail = await db.RecommendationEvents.AsNoTracking()
            .Where(e => e.EventType == RecommendationEventType.Rejected).Select(e => e.Detail).SingleAsync();
        detail.Should().NotBeNull();
        detail!.Should().NotContain("<script").And.NotContain("onerror");
    }

    [Fact]
    public async Task ISSUE149b_Preserves_benign_approval_comment()
    {
        await SeedAsync();
        const string benign = "Approved - within the Q3 envelope (85% utilised), revisit in January.";
        var rec = (await Service(HrUser()).SaveAsync(new SaveRecommendationInput(
            _topPerformerId, _cycleId, RecommendationType.Bonus, Details(bonusAmount: 5000m), null, null))).Value!;
        await Service(HrUser()).SubmitAsync(new SubmitRecommendationInput(rec.Id, new[] { _approverEmpId }, null));

        var approved = await Service(ApproverUser()).DecideAsync(
            new DecideRecommendationInput(rec.Id, true, "  " + benign + "  ", null));
        approved.IsSuccess.Should().BeTrue(approved.Error);

        using var db = Db();
        var comment = await db.RecommendationApprovers.AsNoTracking().Select(a => a.Comment).SingleAsync();
        comment.Should().Be(benign, "an approver's reasoning must be recorded exactly as written");
    }

    // ── NFR-2: tenant scoping ───────────────────────────────────────────

    [Fact]
    public async Task Unresolved_tenant_is_refused()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var svc = new RecommendationService(
            TestDbContextFactory.Create(ctx, _dbName), ctx, HrUser(), new GanssHtmlSanitizer(), _integration,
            Substitute.For<IPayrollAuditLogger>(),
            Substitute.For<ILogger<RecommendationService>>());

        var result = await svc.AutoGenerateAsync(_cycleId);
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }
}
