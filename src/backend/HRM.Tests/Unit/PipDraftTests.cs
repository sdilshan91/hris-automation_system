// ============================================================================
// US-PRF-008 / BUG-244 #7: PIP create-form pre-fill query — PipService.GetDraftAsync unit tests.
//
// Drives PipService.GetDraftAsync through the real EF Core InMemory provider (so employee, job_title,
// manager_review and pip rows are actually persisted and read back through the tenant global query filter).
// Covers the create-form pre-fill contract (US-PRF-008 AC-1):
//   - Prefill of an employee with no active PIP: name / job title / manager resolved, HasActivePip == false,
//     SuggestedReason == null, EscalationOptions = every non-None PipEscalationAction (BR-6).
//   - HasActivePip mirrors the EXACT BR-2 create-path predicate (Draft/Active/Extended = non-terminal); a
//     Closed/NotMet/Cancelled PIP does NOT set it — proving it is not a blanket "any PIP for this employee".
//   - SuggestedReason is seeded from a matching flagged origin manager review (US-PRF-003 SummaryComment); a
//     foreign / non-existent reviewId is tolerated (ignored, reason stays null, no throw) — FE error tolerance.
//   - Blank HR-initiated form (no employeeId): blank draft but WITH escalation options populated.
//   - A foreign / non-existent employeeId → 404 employee_not_found; a second tenant's employee is not resolvable
//     (NFR-2 tenant scoping).
//
// Provider: EF Core InMemory (matches the sibling PipServiceTests harness; NSubstitute for ITenantContext /
// ICurrentUser / IPerformanceNotificationService / ILogger).
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

namespace HRM.Tests.Unit;

public sealed class PipDraftTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    private readonly Guid _hrUserId = Guid.NewGuid();

    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _employeeEmpId = Guid.NewGuid();
    private readonly Guid _jobTitleId = Guid.NewGuid();

    // Every configurable escalation action (BR-6 — everything except the None sentinel).
    private static readonly PipEscalationAction[] AllEscalationOptions =
        Enum.GetValues<PipEscalationAction>().Where(a => a != PipEscalationAction.None).ToArray();

    public PipDraftTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private PipService Service() => new(
        Db(), _tenantContext, HrUser(),
        Substitute.For<IPerformanceNotificationService>(),
        Substitute.For<ILogger<PipService>>());

    private ICurrentUser HrUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(_hrUserId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("hr@t.com");
        u.Permissions.Returns(new[] { PermissionCatalog.Performance.ReviewAll });
        return u;
    }

    /// <summary>Seeds a job title, the manager, and the target employee (reporting to the manager, with that title).</summary>
    private async Task SeedEmployeesAsync()
    {
        using var db = Db();
        db.JobTitles.Add(new JobTitle { Id = _jobTitleId, TenantId = _tenantId, TitleName = "Senior Engineer" });
        db.Employees.Add(new Employee
        {
            Id = _managerEmpId, TenantId = _tenantId, UserId = Guid.NewGuid(), EmployeeNo = "MGR",
            FirstName = "Grace", LastName = "Hopper", Email = "g@t.com", Status = EmployeeStatus.Active,
            JobTitleId = _jobTitleId,
        });
        db.Employees.Add(new Employee
        {
            Id = _employeeEmpId, TenantId = _tenantId, UserId = Guid.NewGuid(), EmployeeNo = "EMP",
            FirstName = "Ada", LastName = "Lovelace", Email = "a@t.com", Status = EmployeeStatus.Active,
            JobTitleId = _jobTitleId, ReportsToEmployeeId = _managerEmpId,
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedPipAsync(Guid employeeId, PipStatus status)
    {
        using var db = Db();
        db.Pips.Add(new Pip
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeId = employeeId,
            Reason = "seeded", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 3, 1),
            Status = status, EscalationAction = PipEscalationAction.TerminationRecommendation,
            AcknowledgementStatus = PipAcknowledgementStatus.Pending,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Seeds a manager review (with the given comment) for an employee and returns its id.</summary>
    private async Task<Guid> SeedReviewAsync(Guid employeeId, string? summaryComment, ReviewFlag flag = ReviewFlag.Pip)
    {
        var reviewId = BaseEntity.NewUuidV7();
        using var db = Db();
        db.ManagerReviews.Add(new ManagerReview
        {
            Id = reviewId, TenantId = _tenantId, CycleId = Guid.NewGuid(), EmployeeId = employeeId,
            Status = ManagerReviewStatus.Submitted, Flag = flag, SummaryComment = summaryComment,
        });
        await db.SaveChangesAsync();
        return reviewId;
    }

    // ── 1. Prefill for an employee with no active PIP ────────────────────

    [Fact]
    public async Task GetDraft_for_employee_with_no_active_pip_prefills_identity_and_options()
    {
        await SeedEmployeesAsync();

        var result = await Service().GetDraftAsync(new GetPipDraftInput(_employeeEmpId, null));

        result.IsSuccess.Should().BeTrue();
        var draft = result.Value!;
        draft.EmployeeId.Should().Be(_employeeEmpId);
        draft.EmployeeName.Should().Be("Ada Lovelace");
        draft.JobTitle.Should().Be("Senior Engineer");
        draft.ManagerName.Should().Be("Grace Hopper");
        draft.HasActivePip.Should().BeFalse();
        draft.SuggestedReason.Should().BeNull();
        draft.EscalationOptions.Should().BeEquivalentTo(AllEscalationOptions);
        draft.EscalationOptions.Should().NotContain(PipEscalationAction.None);
    }

    // ── 2. HasActivePip mirrors the BR-2 create-path predicate ───────────

    [Theory]
    [InlineData(PipStatus.Draft)]
    [InlineData(PipStatus.Active)]
    [InlineData(PipStatus.Extended)]
    public async Task GetDraft_sets_HasActivePip_true_for_non_terminal_pip(PipStatus status)
    {
        await SeedEmployeesAsync();
        await SeedPipAsync(_employeeEmpId, status);

        var result = await Service().GetDraftAsync(new GetPipDraftInput(_employeeEmpId, null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.HasActivePip.Should().BeTrue();
    }

    // Companion: a CLOSED PIP (terminal status) does NOT set HasActivePip — proving GetDraftAsync reuses the
    // exact CreateAsync BR-2 predicate (Draft/Active/Extended) and is not a blanket "any PIP for this employee".
    [Theory]
    [InlineData(PipStatus.SuccessfullyCompleted)]
    [InlineData(PipStatus.NotMet)]
    [InlineData(PipStatus.Cancelled)]
    public async Task GetDraft_leaves_HasActivePip_false_for_terminal_pip(PipStatus status)
    {
        await SeedEmployeesAsync();
        await SeedPipAsync(_employeeEmpId, status);

        var result = await Service().GetDraftAsync(new GetPipDraftInput(_employeeEmpId, null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.HasActivePip.Should().BeFalse();
    }

    // ── 3. SuggestedReason from a matching flagged review ────────────────

    [Fact]
    public async Task GetDraft_seeds_SuggestedReason_from_matching_review_comment()
    {
        await SeedEmployeesAsync();
        // Surrounding whitespace verifies the impl's .Trim() rather than a raw pass-through.
        var reviewId = await SeedReviewAsync(_employeeEmpId, "  Consistent missed deadlines  ");

        var result = await Service().GetDraftAsync(new GetPipDraftInput(_employeeEmpId, reviewId));

        result.IsSuccess.Should().BeTrue();
        result.Value!.SuggestedReason.Should().Be("Consistent missed deadlines");
    }

    // Companion: a reviewId belonging to a DIFFERENT employee is tolerated — the reason stays null, no throw.
    // Paired with the test above this genuinely observes the matching-vs-foreign predicate (r.EmployeeId == employeeId).
    [Fact]
    public async Task GetDraft_ignores_review_belonging_to_a_different_employee()
    {
        await SeedEmployeesAsync();
        // The review is flagged + has a comment, but it is the MANAGER's review, not the target employee's.
        var foreignReviewId = await SeedReviewAsync(_managerEmpId, "This comment must not leak into the draft");

        var result = await Service().GetDraftAsync(new GetPipDraftInput(_employeeEmpId, foreignReviewId));

        result.IsSuccess.Should().BeTrue();
        result.Value!.SuggestedReason.Should().BeNull();
    }

    [Fact]
    public async Task GetDraft_tolerates_a_nonexistent_reviewId()
    {
        await SeedEmployeesAsync();

        var result = await Service().GetDraftAsync(new GetPipDraftInput(_employeeEmpId, Guid.NewGuid()));

        result.IsSuccess.Should().BeTrue();
        result.Value!.SuggestedReason.Should().BeNull();
    }

    // ── 4. Blank HR-initiated form (no employeeId) ───────────────────────

    [Fact]
    public async Task GetDraft_with_no_employee_returns_blank_form_with_escalation_options()
    {
        // No seed needed — the blank form resolves no employee context.
        var result = await Service().GetDraftAsync(new GetPipDraftInput(null, null));

        result.IsSuccess.Should().BeTrue();
        var draft = result.Value!;
        draft.EmployeeId.Should().BeNull();
        draft.EmployeeName.Should().BeNull();
        draft.JobTitle.Should().BeNull();
        draft.ManagerName.Should().BeNull();
        draft.SuggestedReason.Should().BeNull();
        draft.HasActivePip.Should().BeFalse();
        draft.EscalationOptions.Should().BeEquivalentTo(AllEscalationOptions);
    }

    // ── 5. Foreign / non-existent employee → 404 ─────────────────────────

    [Fact]
    public async Task GetDraft_for_nonexistent_employee_returns_404()
    {
        await SeedEmployeesAsync();

        var result = await Service().GetDraftAsync(new GetPipDraftInput(Guid.NewGuid(), null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("employee_not_found");
    }

    // NFR-2 tenant scoping: an employee that exists only under ANOTHER tenant is not resolvable from this tenant
    // (the EF global query filter excludes it), so the draft query 404s exactly as for a non-existent id.
    [Fact]
    public async Task GetDraft_cannot_resolve_a_foreign_tenants_employee()
    {
        var foreignEmpId = Guid.NewGuid();
        using (var db = Db())
        {
            db.Employees.Add(new Employee
            {
                Id = foreignEmpId, TenantId = _otherTenantId, UserId = Guid.NewGuid(), EmployeeNo = "FOR",
                FirstName = "Foreign", LastName = "Person", Email = "f@other.com", Status = EmployeeStatus.Active,
            });
            await db.SaveChangesAsync();
        }

        var result = await Service().GetDraftAsync(new GetPipDraftInput(foreignEmpId, null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("employee_not_found");
    }
}
