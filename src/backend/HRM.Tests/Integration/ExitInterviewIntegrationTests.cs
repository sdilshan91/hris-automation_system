// ============================================================================
// US-ONB-006: Exit-interview recording + analytics integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (with the MediatR
// pipeline, the FluentValidation ValidationBehavior, and the global query filters), covering:
//   - AC-2/FR-3/FR-6: HR-conducted happy path — record against an offboarding instance; responses persisted,
//     tenant_id stamped, linked to the offboarding, and the exit-interview offboarding task completed.
//   - FR-8: self-service mode persists the interview AND writes the HR-notification outbox row.
//   - BR-1: a duplicate exit interview per offboarding is rejected (409) when the caller cannot re-version.
//   - BR-2: a detail-permitted edit creates a NEW version and preserves the original (superseded).
//   - FR-4/FR-5/AC-4: analytics aggregation correctness (reason distribution + average ratings per category),
//     tenant-scoped, aggregates only (no free-text).
//   - AC-5/NFR-2: tenant isolation — a second tenant cannot read another tenant's interview or analytics.
//
// NOTE ON PROVIDER: mirrors OffboardingIntegrationTests — EF Core InMemory through the real composed pipeline
// (no PostgreSQL, no Docker in the verify gate). PG-specific schema is validated by the `migrations` CI job
// applying the generated migration. ExitInterview.ViewDetail / Onboarding.Manage authorization lives in the
// controller; here we drive the handlers directly and pass IsSelfService/AllowEdit as the controller would.
// ============================================================================

using FluentAssertions;
using FluentValidation;
using HRM.Application.Common.Behaviors;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Onboarding.Commands;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Application.Features.Onboarding.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class ExitInterviewIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _hrUserA = Guid.NewGuid();

    private readonly Guid _employeeA = Guid.NewGuid();
    private readonly Guid _employeeUserA = Guid.NewGuid();

    public ExitInterviewIntegrationTests()
    {
        SeedTenantsAndEmployees();
    }

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    private IMediator BuildPipeline(Guid tenantId, Guid userId, string email = "hr@test.com")
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Email.Returns(email);
        currentUser.IsAuthenticated.Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        services.AddScoped<IExitInterviewService, ExitInterviewService>();

        services.AddValidatorsFromAssembly(typeof(RecordExitInterviewCommand).Assembly);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(RecordExitInterviewCommand).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private AppDbContext OpenDb(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private void SeedTenantsAndEmployees()
    {
        using var db = OpenDb(_tenantA);

        db.Tenants.AddRange(
            new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme Corp" },
            new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });

        db.Users.Add(new User { Id = _employeeUserA, Email = "dana@acme.com", IsActive = true });

        db.Employees.Add(new Employee
        {
            Id = _employeeA, TenantId = _tenantA, EmployeeNo = "EMP-0002",
            FirstName = "Dana", LastName = "Departing", Email = "dana@acme.com",
            DateOfJoining = DateTime.UtcNow.AddYears(-2).Date, UserId = _employeeUserA,
            Status = EmployeeStatus.Suspended,
        });

        db.SaveChanges();
    }

    /// <summary>Seeds an in-progress offboarding for tenant A with a default-named exit-interview task.</summary>
    private Guid SeedOffboarding(
        Guid employeeId, OffboardingReason reason = OffboardingReason.Resignation,
        DateTime? lwd = null, bool withExitTask = true)
    {
        var instanceId = BaseEntity.NewUuidV7();
        using var db = OpenDb(_tenantA);
        var instance = new OffboardingInstance
        {
            Id = instanceId, TenantId = _tenantA, EmployeeId = employeeId,
            TemplateName = "Default Exit Clearance",
            LastWorkingDay = lwd ?? DateTime.UtcNow.AddDays(10).Date,
            Reason = reason, Status = OffboardingStatus.InProgress,
            InitiatedByUserId = _hrUserA,
        };
        if (withExitTask)
        {
            instance.Tasks.Add(new OffboardingTaskInstance
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, OffboardingInstanceId = instanceId,
                ClearanceCategory = ClearanceCategory.HR, Title = "Conduct exit interview",
                ResponsibleRole = OnboardingResponsibleRole.HR, DueDate = DateTime.UtcNow.AddDays(5).Date,
                Status = OnboardingTaskStatus.Pending, IsMandatory = false, SortOrder = 0,
            });
        }
        db.OffboardingInstances.Add(instance);
        db.SaveChanges();
        return instanceId;
    }

    private async Task<ExitInterviewTemplateDto> TemplateFor(IMediator mediator) =>
        (await mediator.Send(new GetExitInterviewTemplateQuery())).Value!;

    private static RecordExitInterviewInput Input(
        Guid offboardingId, ExitInterviewTemplateDto template, string mode = "hr_conducted",
        DateTime? date = null, int? overall = 4)
    {
        var ratingQ = template.Categories.SelectMany(c => c.Questions).First(q => q.Type == "rating");
        var freeQ = template.Categories.SelectMany(c => c.Questions).First(q => q.Type == "free_text");
        return new RecordExitInterviewInput(
            offboardingId, mode, date ?? DateTime.UtcNow.Date,
            new[]
            {
                new ExitInterviewResponseInput(ratingQ.QuestionId, 5, null, null),
                new ExitInterviewResponseInput(freeQ.QuestionId, null, null, "It was a great place to work."),
            },
            overall, true, "Thank you.");
    }

    // ── AC-2 / FR-3 / FR-6 HR-conducted happy path ──────────────────────

    [Fact]
    public async Task Record_hr_conducted_persists_responses_stamps_tenant_links_offboarding_and_completes_task()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        var offboardingId = SeedOffboarding(_employeeA);
        var template = await TemplateFor(mediator);

        var result = await mediator.Send(new RecordExitInterviewCommand(
            Input(offboardingId, template), IsSelfService: false, AllowEdit: false));

        result.IsSuccess.Should().BeTrue();
        result.Value!.OffboardingInstanceId.Should().Be(offboardingId);
        result.Value.InterviewMode.Should().Be("hr_conducted");
        result.Value.ConductedByUserId.Should().Be(_hrUserA);
        result.Value.Responses.Should().HaveCount(2);
        result.Value.Version.Should().Be(1);

        await using var db = OpenDb(_tenantA);
        var interview = await db.ExitInterviews.Include(i => i.Responses)
            .SingleAsync(i => i.OffboardingInstanceId == offboardingId);
        interview.TenantId.Should().Be(_tenantA);                              // FR-6 stamped from session.
        interview.Responses.Should().OnlyContain(r => r.TenantId == _tenantA); // FR-6 on children too.
        interview.OffboardingInstanceId.Should().Be(offboardingId);            // FR-3 linkage.

        // AC-2: the exit-interview offboarding task is completed.
        var task = await db.OffboardingTaskInstances.SingleAsync(t => t.OffboardingInstanceId == offboardingId);
        task.Status.Should().Be(OnboardingTaskStatus.Completed);
        task.CompletedAt.Should().NotBeNull();
    }

    // ── FR-8 self-service persists + outbox notification ─────────────────

    [Fact]
    public async Task Record_self_service_persists_and_writes_the_hr_notification_outbox_row()
    {
        // Self-service caller is the departing employee (their user id maps to the employee record).
        var mediator = BuildPipeline(_tenantA, _employeeUserA, email: "dana@acme.com");
        var offboardingId = SeedOffboarding(_employeeA);
        var template = await TemplateFor(mediator);

        var result = await mediator.Send(new RecordExitInterviewCommand(
            Input(offboardingId, template, mode: "self_service"), IsSelfService: true, AllowEdit: false));

        result.IsSuccess.Should().BeTrue();
        result.Value!.InterviewMode.Should().Be("self_service");

        await using var db = OpenDb(_tenantA);
        (await db.ExitInterviews.CountAsync(i => i.OffboardingInstanceId == offboardingId)).Should().Be(1);

        // FR-8: a self-service submission notifies HR via the outbox.
        var outbox = await db.OnboardingNotificationOutbox.SingleAsync(o => o.ChecklistInstanceId == offboardingId);
        outbox.TenantId.Should().Be(_tenantA);
        outbox.RecipientUserId.Should().Be(_hrUserA); // the offboarding initiator (HR).
        outbox.NotificationType.Should().Be("exit_interview.self_service.submitted");
        outbox.Status.Should().Be(OnboardingNotificationStatus.Pending);
    }

    // ── BR-1 duplicate rejected ─────────────────────────────────────────

    [Fact]
    public async Task Record_a_second_interview_for_the_same_offboarding_without_edit_is_rejected_br1()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        var offboardingId = SeedOffboarding(_employeeA);
        var template = await TemplateFor(mediator);

        var first = await mediator.Send(new RecordExitInterviewCommand(
            Input(offboardingId, template), IsSelfService: false, AllowEdit: false));
        first.IsSuccess.Should().BeTrue();

        var second = await mediator.Send(new RecordExitInterviewCommand(
            Input(offboardingId, template), IsSelfService: false, AllowEdit: false));

        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("exit_interview_exists");

        await using var db = OpenDb(_tenantA);
        (await db.ExitInterviews.CountAsync(i => i.OffboardingInstanceId == offboardingId)).Should().Be(1);
    }

    // ── BR-2 immutability / versioning on edit ──────────────────────────

    [Fact]
    public async Task Record_with_edit_permission_creates_a_new_version_and_preserves_the_original_br2()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        var offboardingId = SeedOffboarding(_employeeA);
        var template = await TemplateFor(mediator);

        var first = await mediator.Send(new RecordExitInterviewCommand(
            Input(offboardingId, template, overall: 3), IsSelfService: false, AllowEdit: false));
        first.IsSuccess.Should().BeTrue();

        var edited = await mediator.Send(new RecordExitInterviewCommand(
            Input(offboardingId, template, overall: 5), IsSelfService: false, AllowEdit: true));

        edited.IsSuccess.Should().BeTrue();
        edited.Value!.Version.Should().Be(2);
        edited.Value.OverallExperienceRating.Should().Be(5);

        await using var db = OpenDb(_tenantA);
        var all = await db.ExitInterviews.IgnoreQueryFilters()
            .Where(i => i.OffboardingInstanceId == offboardingId).ToListAsync();
        all.Should().HaveCount(2); // BR-2: original preserved, not overwritten.
        all.Single(i => i.Version == 1).IsSuperseded.Should().BeTrue();
        var active = all.Single(i => i.Version == 2);
        active.IsSuperseded.Should().BeFalse();
        active.SupersedesId.Should().Be(all.Single(i => i.Version == 1).Id);

        // Only the active version is returned by the read through the pipeline.
        var read = await mediator.Send(new GetExitInterviewByOffboardingQuery(offboardingId, IncludeFreeText: true));
        read.Value!.Version.Should().Be(2);
    }

    // ── FR-4 / FR-5 / AC-4 analytics aggregation ────────────────────────

    [Fact]
    public async Task Analytics_aggregates_reason_distribution_and_average_ratings_per_category()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);

        // A second departing employee for a Termination offboarding.
        var employeeB = Guid.NewGuid();
        await using (var db = OpenDb(_tenantA))
        {
            db.Employees.Add(new Employee
            {
                Id = employeeB, TenantId = _tenantA, EmployeeNo = "EMP-0003",
                FirstName = "Eve", LastName = "Exiting", Email = "eve@acme.com",
                DateOfJoining = DateTime.UtcNow.AddYears(-1).Date, Status = EmployeeStatus.Suspended,
            });
            await db.SaveChangesAsync();
        }

        var off1 = SeedOffboarding(_employeeA, OffboardingReason.Resignation);
        var off2 = SeedOffboarding(employeeB, OffboardingReason.Termination);
        var template = await TemplateFor(mediator);

        await mediator.Send(new RecordExitInterviewCommand(
            Input(off1, template, overall: 4), IsSelfService: false, AllowEdit: false));
        await mediator.Send(new RecordExitInterviewCommand(
            Input(off2, template, overall: 2), IsSelfService: false, AllowEdit: false));

        var result = await mediator.Send(new GetExitInterviewAnalyticsQuery(null, null, HasDetailPermission: false));

        result.IsSuccess.Should().BeTrue();
        // Reason distribution: one Resignation + one Termination.
        result.Value!.ReasonDistribution.Select(r => r.Reason)
            .Should().BeEquivalentTo(new[] { "Resignation", "Termination" });
        result.Value.ReasonDistribution.Should().OnlyContain(r => r.Count == 1);

        // Average rating per category: both interviews answered the rating question with 5.
        result.Value.AverageRatingsPerCategory.Should().NotBeEmpty();
        result.Value.AverageRatingsPerCategory.Should().OnlyContain(c => c.AverageRating == 5);

        // Trend: a single monthly bucket with both interviews; avg overall = (4 + 2) / 2 = 3.
        result.Value.Trend.Should().ContainSingle();
        result.Value.Trend.Single().Count.Should().Be(2);
        result.Value.Trend.Single().AverageRating.Should().Be(3);
    }

    // ── AC-5 / NFR-2 tenant isolation ───────────────────────────────────

    [Fact]
    public async Task Tenant_B_cannot_read_tenant_A_exit_interview()
    {
        var hrA = BuildPipeline(_tenantA, _hrUserA);
        var offboardingId = SeedOffboarding(_employeeA);
        var template = await TemplateFor(hrA);
        (await hrA.Send(new RecordExitInterviewCommand(
            Input(offboardingId, template), IsSelfService: false, AllowEdit: false))).IsSuccess.Should().BeTrue();

        var hrB = BuildPipeline(_tenantB, Guid.NewGuid());
        var read = await hrB.Send(new GetExitInterviewByOffboardingQuery(offboardingId, IncludeFreeText: true));

        read.IsFailure.Should().BeTrue();
        read.StatusCode.Should().Be(404);
        read.ErrorCode.Should().Be("exit_interview_not_found");
    }

    [Fact]
    public async Task Tenant_B_analytics_does_not_include_tenant_A_interviews()
    {
        var hrA = BuildPipeline(_tenantA, _hrUserA);
        var offboardingId = SeedOffboarding(_employeeA);
        var template = await TemplateFor(hrA);
        (await hrA.Send(new RecordExitInterviewCommand(
            Input(offboardingId, template), IsSelfService: false, AllowEdit: false))).IsSuccess.Should().BeTrue();

        var hrB = BuildPipeline(_tenantB, Guid.NewGuid());
        var result = await hrB.Send(new GetExitInterviewAnalyticsQuery(null, null, HasDetailPermission: false));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ReasonDistribution.Should().BeEmpty();
        result.Value.AverageRatingsPerCategory.Should().BeEmpty();
        result.Value.Trend.Should().BeEmpty();
    }
}
