// ============================================================================
// US-ONB-006: ExitInterviewService unit tests.
//
// Drives ExitInterviewService through the real EF Core InMemory provider (so the template, interviews,
// responses, the onboarding notification outbox, and the global query filter actually apply). Covers:
//   - FR-1/AC-1: GetTemplate seeds + persists the DEFAULT questionnaire (4 categories) when none exists,
//     and re-uses the persisted template on subsequent calls.
//   - AC-2/FR-3/FR-6: HR-conducted record persists responses with the session tenant_id, links to the
//     offboarding, and completes the exit-interview offboarding task.
//   - FR-8: a self-service record writes an HR-notification outbox row.
//   - BR-1: a second record for the same offboarding is rejected (duplicate) unless allowEdit.
//   - BR-2: allowEdit creates a NEW version and preserves the original (superseded).
//   - BR-3: self-service after the last working day is rejected; self-service on another's offboarding is 403.
//   - FR-4/FR-5: analytics returns aggregates only (reason distribution, average ratings, trend) and never
//     includes free-text; tenant-scoped.
//   - NFR-2/AC-5: tenant isolation on reads + analytics.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class ExitInterviewServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _hrUser;

    private readonly Guid _hrUserId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();

    public ExitInterviewServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _hrUser = Substitute.For<ICurrentUser>();
        _hrUser.UserId.Returns(_hrUserId);
        _hrUser.Email.Returns("hr@acme.com");
        _hrUser.IsAuthenticated.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private ExitInterviewService Service(ICurrentUser? user = null) =>
        new(Db(), _tenantContext, user ?? _hrUser, Substitute.For<ILogger<ExitInterviewService>>());

    /// <summary>Seeds a departing employee + an in-progress offboarding (with a default-named exit interview task).</summary>
    private Guid SeedOffboarding(DateOnly? lwd = null, bool withExitTask = true)
    {
        var instanceId = BaseEntity.NewUuidV7();
        using var db = Db();
        db.Users.Add(new User { Id = _employeeUserId, Email = "dana@acme.com", IsActive = true });
        db.Employees.Add(new Employee
        {
            Id = _employeeId, TenantId = _tenantId, EmployeeNo = "EMP-0002",
            FirstName = "Dana", LastName = "Departing", Email = "dana@acme.com",
            DateOfJoining = DateTime.UtcNow.AddYears(-2).Date, UserId = _employeeUserId,
            Status = EmployeeStatus.Suspended,
        });
        var instance = new OffboardingInstance
        {
            Id = instanceId, TenantId = _tenantId, EmployeeId = _employeeId,
            TemplateName = "Default Exit Clearance",
            LastWorkingDay = lwd ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            Reason = OffboardingReason.Resignation, Status = OffboardingStatus.InProgress,
            InitiatedByUserId = _hrUserId,
        };
        if (withExitTask)
        {
            instance.Tasks.Add(new OffboardingTaskInstance
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, OffboardingInstanceId = instanceId,
                ClearanceCategory = ClearanceCategory.HR, Title = "Conduct exit interview",
                ResponsibleRole = OnboardingResponsibleRole.HR, DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
                Status = OnboardingTaskStatus.Pending, IsMandatory = false, SortOrder = 0,
            });
        }
        db.OffboardingInstances.Add(instance);
        db.SaveChanges();
        return instanceId;
    }

    private async Task<ExitInterviewTemplateDto> Template() => (await Service().GetTemplateAsync()).Value!;

    private static RecordExitInterviewInput Input(
        Guid offboardingId, ExitInterviewTemplateDto template, string mode = "hr_conducted",
        DateOnly? date = null, int? overall = 4)
    {
        var ratingQ = template.Categories.SelectMany(c => c.Questions).First(q => q.Type == "rating");
        var freeQ = template.Categories.SelectMany(c => c.Questions).First(q => q.Type == "free_text");
        return new RecordExitInterviewInput(
            offboardingId, mode, date ?? DateOnly.FromDateTime(DateTime.UtcNow),
            new[]
            {
                new ExitInterviewResponseInput(ratingQ.QuestionId, 5, null, null),
                new ExitInterviewResponseInput(freeQ.QuestionId, null, null, "It was a great place to work."),
            },
            overall, true, "Thank you.");
    }

    // ── FR-1 / AC-1 template ────────────────────────────────────────────

    [Fact]
    public async Task GetTemplate_seeds_default_questionnaire_with_the_four_ac1_categories()
    {
        var result = await Service().GetTemplateAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Categories.Select(c => c.Category)
            .Should().BeEquivalentTo(new[]
            {
                "Reason for Leaving", "Work Environment", "Management", "Recommendations",
            });
        result.Value.Categories.SelectMany(c => c.Questions).Should().NotBeEmpty();

        // FR-1: the question types use the snake_case wire values.
        var types = result.Value.Categories.SelectMany(c => c.Questions).Select(q => q.Type).Distinct();
        types.Should().BeSubsetOf(new[] { "rating", "multiple_choice", "free_text", "yes_no" });

        using var db = Db();
        db.ExitInterviewTemplates.Count().Should().Be(1); // persisted for the tenant.
    }

    [Fact]
    public async Task GetTemplate_reuses_the_persisted_template_on_subsequent_calls()
    {
        var first = (await Service().GetTemplateAsync()).Value!;
        var second = (await Service().GetTemplateAsync()).Value!;

        second.TemplateId.Should().Be(first.TemplateId);
        using var db = Db();
        db.ExitInterviewTemplates.Count().Should().Be(1);
    }

    // ── AC-2 / FR-3 / FR-6 HR-conducted record ──────────────────────────

    [Fact]
    public async Task Record_hr_conducted_persists_responses_with_tenant_id_and_completes_the_exit_task()
    {
        var offboardingId = SeedOffboarding();
        var template = await Template();

        var result = await Service().RecordAsync(
            Input(offboardingId, template), isSelfService: false, allowEdit: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.OffboardingInstanceId.Should().Be(offboardingId);
        result.Value.InterviewMode.Should().Be("hr_conducted");
        result.Value.ConductedByUserId.Should().Be(_hrUserId);
        result.Value.Responses.Should().HaveCount(2);

        using var db = Db();
        var interview = db.ExitInterviews.Include(i => i.Responses).Single();
        interview.TenantId.Should().Be(_tenantId);            // FR-6 stamped from session.
        interview.Responses.Should().OnlyContain(r => r.TenantId == _tenantId);
        interview.Version.Should().Be(1);

        // AC-2: the exit-interview offboarding task is completed.
        var task = db.OffboardingTaskInstances.Single();
        task.Status.Should().Be(OnboardingTaskStatus.Completed);
        task.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Record_for_unknown_offboarding_returns_404()
    {
        var template = await Template();

        var result = await Service().RecordAsync(
            Input(Guid.NewGuid(), template), isSelfService: false, allowEdit: false);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("offboarding_not_found");
    }

    [Fact]
    public async Task Record_with_a_question_not_in_the_template_is_rejected()
    {
        var offboardingId = SeedOffboarding();
        await Template();

        var bad = new RecordExitInterviewInput(
            offboardingId, "hr_conducted", DateOnly.FromDateTime(DateTime.UtcNow),
            new[] { new ExitInterviewResponseInput(Guid.NewGuid(), 3, null, null) },
            4, true, null);

        var result = await Service().RecordAsync(bad, isSelfService: false, allowEdit: false);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("unknown_question");
    }

    // ── BR-1 duplicate ──────────────────────────────────────────────────

    [Fact]
    public async Task Record_a_second_interview_without_edit_permission_is_rejected_br1()
    {
        var offboardingId = SeedOffboarding();
        var template = await Template();
        await Service().RecordAsync(Input(offboardingId, template), isSelfService: false, allowEdit: false);

        var second = await Service().RecordAsync(
            Input(offboardingId, template), isSelfService: false, allowEdit: false);

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("exit_interview_exists");
    }

    // ── BR-2 immutability / versioning ──────────────────────────────────

    [Fact]
    public async Task Record_with_edit_permission_creates_a_new_version_and_preserves_the_original_br2()
    {
        var offboardingId = SeedOffboarding();
        var template = await Template();
        var first = await Service().RecordAsync(
            Input(offboardingId, template, overall: 3), isSelfService: false, allowEdit: false);
        first.IsSuccess.Should().BeTrue();

        var edited = await Service().RecordAsync(
            Input(offboardingId, template, overall: 5), isSelfService: false, allowEdit: true);

        edited.IsSuccess.Should().BeTrue();
        edited.Value!.Version.Should().Be(2);
        edited.Value.OverallExperienceRating.Should().Be(5);

        using var db = Db();
        // BR-2: both rows exist; the original is preserved + superseded.
        var all = db.ExitInterviews.IgnoreQueryFilters()
            .Where(i => i.OffboardingInstanceId == offboardingId).ToList();
        all.Should().HaveCount(2);
        all.Single(i => i.Version == 1).IsSuperseded.Should().BeTrue();
        var active = all.Single(i => i.Version == 2);
        active.IsSuperseded.Should().BeFalse();
        active.SupersedesId.Should().Be(all.Single(i => i.Version == 1).Id);

        // Only the active version is returned by the read.
        var read = await Service().GetByOffboardingAsync(offboardingId, includeFreeText: true);
        read.Value!.Version.Should().Be(2);
    }

    // ── FR-8 self-service notification + BR-3 deadline ──────────────────

    [Fact]
    public async Task Record_self_service_writes_an_hr_notification_outbox_row()
    {
        var offboardingId = SeedOffboarding();
        var template = await Template();

        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.UserId.Returns(_employeeUserId);
        employeeUser.Email.Returns("dana@acme.com");
        employeeUser.IsAuthenticated.Returns(true);

        var result = await Service(employeeUser).RecordAsync(
            Input(offboardingId, template, mode: "self_service"), isSelfService: true, allowEdit: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.InterviewMode.Should().Be("self_service");

        using var db = Db();
        var outbox = db.OnboardingNotificationOutbox.Single();
        outbox.TenantId.Should().Be(_tenantId);
        outbox.RecipientUserId.Should().Be(_hrUserId); // the offboarding initiator (HR).
        outbox.NotificationType.Should().Be("exit_interview.self_service.submitted");
        outbox.Status.Should().Be(OnboardingNotificationStatus.Pending);
    }

    [Fact]
    public async Task Record_self_service_after_last_working_day_is_rejected_br3()
    {
        var offboardingId = SeedOffboarding(lwd: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)));
        var template = await Template();

        var employeeUser = Substitute.For<ICurrentUser>();
        employeeUser.UserId.Returns(_employeeUserId);
        employeeUser.IsAuthenticated.Returns(true);

        var result = await Service(employeeUser).RecordAsync(
            Input(offboardingId, template, mode: "self_service"), isSelfService: true, allowEdit: false);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("after_last_working_day");
    }

    [Fact]
    public async Task Record_self_service_for_another_employees_offboarding_is_forbidden()
    {
        var offboardingId = SeedOffboarding();
        var template = await Template();

        // A caller whose user id maps to no employee (or a different one) cannot submit.
        var otherUser = Substitute.For<ICurrentUser>();
        otherUser.UserId.Returns(Guid.NewGuid());
        otherUser.IsAuthenticated.Returns(true);

        var result = await Service(otherUser).RecordAsync(
            Input(offboardingId, template, mode: "self_service"), isSelfService: true, allowEdit: false);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("not_own_offboarding");
    }

    // ── FR-3 read free-text gating (FR-5/NFR-6) ─────────────────────────

    [Fact]
    public async Task GetByOffboarding_without_detail_permission_omits_free_text()
    {
        var offboardingId = SeedOffboarding();
        var template = await Template();
        await Service().RecordAsync(Input(offboardingId, template), isSelfService: false, allowEdit: false);

        var withoutDetail = await Service().GetByOffboardingAsync(offboardingId, includeFreeText: false);
        withoutDetail.Value!.AdditionalComments.Should().BeNull();
        withoutDetail.Value.Responses.Should().OnlyContain(r => r.FreeText == null);

        var withDetail = await Service().GetByOffboardingAsync(offboardingId, includeFreeText: true);
        withDetail.Value!.AdditionalComments.Should().Be("Thank you.");
        withDetail.Value.Responses.Should().Contain(r => r.FreeText != null);
    }

    [Fact]
    public async Task GetByOffboarding_when_none_recorded_returns_404()
    {
        var offboardingId = SeedOffboarding();

        var result = await Service().GetByOffboardingAsync(offboardingId, includeFreeText: false);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("exit_interview_not_found");
    }

    // ── FR-4 / FR-5 analytics ───────────────────────────────────────────

    [Fact]
    public async Task Analytics_aggregates_reason_distribution_and_average_ratings_anonymized()
    {
        // Two offboardings (Resignation + Termination), each with a recorded interview.
        var off1 = SeedOffboarding();
        var off2 = SeedSecondOffboarding(OffboardingReason.Termination);
        var template = await Template();

        await Service().RecordAsync(Input(off1, template, overall: 4), isSelfService: false, allowEdit: false);
        await Service().RecordAsync(Input(off2, template, overall: 2), isSelfService: false, allowEdit: false);

        var result = await Service().GetAnalyticsAsync(null, null, hasDetailPermission: false);

        result.IsSuccess.Should().BeTrue();
        // Reason distribution covers both reasons.
        result.Value!.ReasonDistribution.Select(r => r.Reason)
            .Should().BeEquivalentTo(new[] { "Resignation", "Termination" });
        result.Value.ReasonDistribution.Should().OnlyContain(r => r.Count == 1);

        // Average ratings per category: each interview answered the rating question with 5.
        result.Value.AverageRatingsPerCategory.Should().NotBeEmpty();
        result.Value.AverageRatingsPerCategory.Should().OnlyContain(c => c.AverageRating == 5);

        // Trend has a single monthly bucket with both interviews; avg overall = (4 + 2) / 2 = 3.
        result.Value.Trend.Should().ContainSingle();
        result.Value.Trend.Single().Count.Should().Be(2);
        result.Value.Trend.Single().AverageRating.Should().Be(3);
    }

    // ── NFR-2 / AC-5 tenant isolation ───────────────────────────────────

    [Fact]
    public async Task Analytics_does_not_include_another_tenants_interviews()
    {
        var off1 = SeedOffboarding();
        var template = await Template();
        await Service().RecordAsync(Input(off1, template), isSelfService: false, allowEdit: false);

        // A service scoped to a different tenant sees no data.
        var otherTenant = Substitute.For<ITenantContext>();
        otherTenant.TenantId.Returns(Guid.NewGuid());
        otherTenant.IsResolved.Returns(true);
        var otherService = new ExitInterviewService(
            TestDbContextFactory.Create(otherTenant, _dbName), otherTenant, _hrUser,
            Substitute.For<ILogger<ExitInterviewService>>());

        var result = await otherService.GetAnalyticsAsync(null, null, hasDetailPermission: false);

        result.IsSuccess.Should().BeTrue();
        result.Value!.ReasonDistribution.Should().BeEmpty();
        result.Value.Trend.Should().BeEmpty();
    }

    private Guid SeedSecondOffboarding(OffboardingReason reason)
    {
        var id = BaseEntity.NewUuidV7();
        var empId = Guid.NewGuid();
        using var db = Db();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = _tenantId, EmployeeNo = "EMP-0003",
            FirstName = "Eve", LastName = "Exiting", Email = "eve@acme.com",
            DateOfJoining = DateTime.UtcNow.AddYears(-1).Date, Status = EmployeeStatus.Suspended,
        });
        db.OffboardingInstances.Add(new OffboardingInstance
        {
            Id = id, TenantId = _tenantId, EmployeeId = empId, TemplateName = "Default Exit Clearance",
            LastWorkingDay = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)), Reason = reason,
            Status = OffboardingStatus.InProgress, InitiatedByUserId = _hrUserId,
        });
        db.SaveChanges();
        return id;
    }
}
