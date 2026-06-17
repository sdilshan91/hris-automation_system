// ============================================================================
// US-ONB-002: OnboardingChecklistService unit tests.
//
// Drives OnboardingChecklistService through the real EF Core InMemory provider (so instances, task
// instances, and outbox rows are actually persisted and the global query filter applies). Covers:
//   - AC-2/FR-2/FR-7: assign creates an instance + task instances with calculated due dates, pending
//     status, and the session tenant_id; outbox rows are written (NFR-3).
//   - AC-1/FR-1: applicable-templates filters by dept + job title + universal, active only (BR-1).
//   - AC-3/BR-2: replace supersedes the prior active checklist (new version); merge adds onto it.
//   - BR-4: a past joining date anchors due dates to today.
//   - FR-3: responsible-party resolution (Manager -> manager's user, HR -> assigning user, Employee).
//   - AC-4/FR-5/FR-6/BR-3: modify adds ad-hoc tasks, edits due dates, soft-deletes non-mandatory tasks,
//     and refuses to remove mandatory tasks.
//   - Tenant isolation: another tenant cannot read the instance.
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

public sealed class OnboardingChecklistServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _user;
    private readonly Guid _hrUserId = Guid.NewGuid();

    private readonly Guid _deptId = Guid.NewGuid();
    private readonly Guid _jobTitleId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();

    public OnboardingChecklistServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _user = Substitute.For<ICurrentUser>();
        _user.UserId.Returns(_hrUserId);
        _user.Email.Returns("hr@acme.com");
        _user.IsAuthenticated.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private OnboardingChecklistService Service() =>
        new(Db(), _tenantContext, _user, Substitute.For<ILogger<OnboardingChecklistService>>());

    /// <summary>Seeds a manager + a new hire (joining in the future) in the tenant.</summary>
    private void SeedEmployees(DateTime? joining = null)
    {
        using var db = Db();
        db.Employees.AddRange(
            new Employee
            {
                Id = _managerId, TenantId = _tenantId, EmployeeNo = "EMP-0001",
                FirstName = "Mona", LastName = "Manager", Email = "mona@acme.com",
                DepartmentId = _deptId, JobTitleId = _jobTitleId, DateOfJoining = DateTime.UtcNow.AddYears(-2),
                UserId = _managerUserId,
            },
            new Employee
            {
                Id = _employeeId, TenantId = _tenantId, EmployeeNo = "EMP-0002",
                FirstName = "Nora", LastName = "Newhire", Email = "nora@acme.com",
                DepartmentId = _deptId, JobTitleId = _jobTitleId,
                DateOfJoining = (joining ?? DateTime.UtcNow.AddDays(10)).Date,
                UserId = _employeeUserId, ReportsToEmployeeId = _managerId,
            });
        db.SaveChanges();
    }

    /// <summary>Seeds an active template applicable to the seeded department + job title, returns its id.</summary>
    private Guid SeedTemplate(
        bool isActive = true,
        IReadOnlyList<Guid>? depts = null,
        IReadOnlyList<Guid>? jobTitles = null,
        string name = "Standard Onboarding")
    {
        var templateId = BaseEntity.NewUuidV7();
        using var db = Db();
        db.OnboardingChecklistTemplates.Add(new OnboardingChecklistTemplate
        {
            Id = templateId, TenantId = _tenantId, TemplateName = name, IsActive = isActive,
            ApplicableDepartments = (depts ?? new List<Guid>()).ToList(),
            ApplicableJobTitles = (jobTitles ?? new List<Guid>()).ToList(),
            Tasks = new List<OnboardingTemplateTask>
            {
                new()
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TemplateId = templateId,
                    Title = "Sign contract", Category = "Docs", ResponsibleRole = OnboardingResponsibleRole.HR,
                    DueOffsetDays = 0, IsMandatory = true, SortOrder = 0,
                },
                new()
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TemplateId = templateId,
                    Title = "Provision laptop", Category = "IT", ResponsibleRole = OnboardingResponsibleRole.IT,
                    DueOffsetDays = 2, IsMandatory = false, SortOrder = 1,
                },
                new()
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, TemplateId = templateId,
                    Title = "Welcome lunch", Category = "Culture", ResponsibleRole = OnboardingResponsibleRole.Manager,
                    DueOffsetDays = 3, IsMandatory = false, SortOrder = 2,
                },
            },
        });
        db.SaveChanges();
        return templateId;
    }

    private AssignChecklistInput Assign(
        Guid templateId, ChecklistAssignmentMode mode = ChecklistAssignmentMode.Replace,
        DateTime? overrideStart = null, string? idempotencyKey = null,
        IReadOnlyList<AdHocTaskInput>? adhoc = null) =>
        new(_employeeId, templateId, overrideStart, mode, adhoc ?? Array.Empty<AdHocTaskInput>(), idempotencyKey);

    // ── AC-2 / FR-2 / FR-7 happy path ───────────────────────────────────

    [Fact]
    public async Task Assign_creates_instance_with_due_dates_pending_and_tenant_id()
    {
        var joining = DateTime.UtcNow.AddDays(10).Date;
        SeedEmployees(joining);
        var templateId = SeedTemplate();

        var result = await Service().AssignAsync(Assign(templateId));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.TaskCount.Should().Be(3);
        dto.Status.Should().Be(OnboardingChecklistStatus.Active);
        dto.StartDate.Should().Be(joining);
        dto.Tasks.Should().OnlyContain(t => t.Status == OnboardingTaskStatus.Pending);

        // FR-2: due_date = start_date + due_offset_days.
        dto.Tasks.Single(t => t.Title == "Sign contract").DueDate.Should().Be(joining);
        dto.Tasks.Single(t => t.Title == "Provision laptop").DueDate.Should().Be(joining.AddDays(2));
        dto.Tasks.Single(t => t.Title == "Welcome lunch").DueDate.Should().Be(joining.AddDays(3));

        await using var db = Db();
        var stored = await db.OnboardingChecklistInstances.Include(c => c.Tasks).FirstAsync(c => c.Id == dto.Id);
        stored.TenantId.Should().Be(_tenantId);                       // FR-7
        stored.Tasks.Should().OnlyContain(t => t.TenantId == _tenantId);
    }

    [Fact]
    public async Task Assign_writes_outbox_rows_nfr3()
    {
        SeedEmployees();
        var templateId = SeedTemplate();

        var result = await Service().AssignAsync(Assign(templateId));
        result.IsSuccess.Should().BeTrue();

        await using var db = Db();
        var outbox = await db.OnboardingNotificationOutbox
            .Where(o => o.ChecklistInstanceId == result.Value!.Id).ToListAsync();

        // Recipients: the new hire (Employee) + the manager. (No IT role users seeded.)
        outbox.Should().NotBeEmpty();
        outbox.Should().OnlyContain(o => o.Status == OnboardingNotificationStatus.Pending);
        outbox.Should().OnlyContain(o => o.TenantId == _tenantId);
        outbox.Select(o => o.RecipientUserId).Should().Contain(new[] { _employeeUserId, _managerUserId });
        outbox.Should().OnlyContain(o => o.NotificationType == "onboarding.checklist.assigned");
    }

    [Fact]
    public async Task Assign_resolves_responsible_parties_fr3()
    {
        SeedEmployees();
        var templateId = SeedTemplate();

        var dto = (await Service().AssignAsync(Assign(templateId))).Value!;

        // HR task -> assigning user; Manager task -> manager's linked user.
        dto.Tasks.Single(t => t.ResponsibleRole == OnboardingResponsibleRole.HR)
            .ResponsibleUserId.Should().Be(_hrUserId);
        dto.Tasks.Single(t => t.ResponsibleRole == OnboardingResponsibleRole.Manager)
            .ResponsibleUserId.Should().Be(_managerUserId);
    }

    // ── BR-4 past joining date ──────────────────────────────────────────

    [Fact]
    public async Task Assign_with_past_joining_date_anchors_to_today_br4()
    {
        SeedEmployees(DateTime.UtcNow.AddDays(-10));
        var templateId = SeedTemplate();

        var dto = (await Service().AssignAsync(Assign(templateId))).Value!;

        var today = DateTime.UtcNow.Date;
        dto.StartDate.Should().Be(today);
        dto.Tasks.Single(t => t.Title == "Provision laptop").DueDate.Should().Be(today.AddDays(2));
    }

    // ── AC-1 / FR-1 applicable templates ────────────────────────────────

    [Fact]
    public async Task ApplicableTemplates_filters_by_dept_jobtitle_and_universal_active_only()
    {
        SeedEmployees();
        var universalId = SeedTemplate(name: "Universal");
        var deptMatchId = SeedTemplate(depts: new[] { _deptId }, name: "Dept Match");
        var otherDeptId = SeedTemplate(depts: new[] { Guid.NewGuid() }, name: "Other Dept");
        var inactiveId = SeedTemplate(isActive: false, name: "Inactive");

        var result = await Service().GetApplicableTemplatesAsync(_employeeId);

        result.IsSuccess.Should().BeTrue();
        var ids = result.Value!.Select(t => t.Id).ToList();
        ids.Should().Contain(new[] { universalId, deptMatchId });
        ids.Should().NotContain(otherDeptId);  // FR-1 dept mismatch
        ids.Should().NotContain(inactiveId);    // BR-1 inactive excluded
    }

    // ── AC-3 / BR-2 replace + merge ─────────────────────────────────────

    [Fact]
    public async Task Assign_replace_supersedes_prior_active_and_bumps_version_br2()
    {
        SeedEmployees();
        var templateId = SeedTemplate();

        var first = (await Service().AssignAsync(Assign(templateId))).Value!;
        var second = (await Service().AssignAsync(Assign(templateId, ChecklistAssignmentMode.Replace))).Value!;

        second.Id.Should().NotBe(first.Id);
        second.Version.Should().Be(2);

        await using var db = Db();
        var prior = await db.OnboardingChecklistInstances.IgnoreQueryFilters().FirstAsync(c => c.Id == first.Id);
        prior.Status.Should().Be(OnboardingChecklistStatus.Superseded);

        // BR-2: exactly one active remains.
        var activeCount = await db.OnboardingChecklistInstances
            .CountAsync(c => c.EmployeeId == _employeeId && c.Status == OnboardingChecklistStatus.Active);
        activeCount.Should().Be(1);
    }

    [Fact]
    public async Task Assign_merge_adds_tasks_onto_existing_active()
    {
        SeedEmployees();
        var templateId = SeedTemplate();

        var first = (await Service().AssignAsync(Assign(templateId))).Value!;
        var merged = (await Service().AssignAsync(Assign(templateId, ChecklistAssignmentMode.Merge))).Value!;

        merged.Id.Should().Be(first.Id);          // same instance
        merged.TaskCount.Should().Be(6);           // 3 + 3
        merged.Version.Should().Be(1);
    }

    // ── NFR-5 idempotency ───────────────────────────────────────────────

    [Fact]
    public async Task Assign_with_same_idempotency_key_does_not_duplicate()
    {
        SeedEmployees();
        var templateId = SeedTemplate();

        var first = await Service().AssignAsync(Assign(templateId, idempotencyKey: "key-1"));
        var retry = await Service().AssignAsync(Assign(templateId, idempotencyKey: "key-1"));

        first.IsSuccess.Should().BeTrue();
        retry.IsSuccess.Should().BeTrue();
        retry.Value!.Id.Should().Be(first.Value!.Id);

        await using var db = Db();
        var count = await db.OnboardingChecklistInstances.CountAsync(c => c.EmployeeId == _employeeId);
        count.Should().Be(1);
    }

    // ── Inactive template + missing refs ────────────────────────────────

    [Fact]
    public async Task Assign_inactive_template_is_rejected_br1()
    {
        SeedEmployees();
        var templateId = SeedTemplate(isActive: false);

        var result = await Service().AssignAsync(Assign(templateId));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("template_inactive");
    }

    [Fact]
    public async Task Assign_missing_employee_returns_404()
    {
        var templateId = SeedTemplate();

        var result = await Service().AssignAsync(
            new AssignChecklistInput(Guid.NewGuid(), templateId, null, ChecklistAssignmentMode.Replace,
                Array.Empty<AdHocTaskInput>(), null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("employee_not_found");
    }

    // ── AC-4 / FR-5 / FR-6 / BR-3 modify ────────────────────────────────

    [Fact]
    public async Task Modify_adds_adhoc_task_changes_due_date_and_soft_deletes_nonmandatory()
    {
        SeedEmployees();
        var templateId = SeedTemplate();
        var instance = (await Service().AssignAsync(Assign(templateId))).Value!;

        var laptop = instance.Tasks.Single(t => t.Title == "Provision laptop"); // non-mandatory
        var newDue = DateTime.UtcNow.AddDays(20).Date;

        var result = await Service().ModifyAsync(new ModifyChecklistInput(
            instance.Id,
            AddTasks: new[]
            {
                new AdHocTaskInput("Custom task", "ad-hoc", "Misc", OnboardingResponsibleRole.HR, null, 5, false, 9),
            },
            TaskChanges: new[]
            {
                new ModifyTaskInput(instance.Tasks.Single(t => t.Title == "Welcome lunch").Id, newDue, false),
                new ModifyTaskInput(laptop.Id, null, Remove: true),
            }));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.Tasks.Should().Contain(t => t.Title == "Custom task" && t.SourceTemplateTaskId == null);  // FR-5
        dto.Tasks.Single(t => t.Title == "Welcome lunch").DueDate.Should().Be(newDue);                 // FR-6
        dto.Tasks.Should().NotContain(t => t.Title == "Provision laptop");                             // AC-4 soft-delete
    }

    [Fact]
    public async Task Modify_cannot_remove_mandatory_task_br3()
    {
        SeedEmployees();
        var templateId = SeedTemplate();
        var instance = (await Service().AssignAsync(Assign(templateId))).Value!;
        var mandatory = instance.Tasks.Single(t => t.Title == "Sign contract"); // mandatory

        var result = await Service().ModifyAsync(new ModifyChecklistInput(
            instance.Id, Array.Empty<AdHocTaskInput>(),
            new[] { new ModifyTaskInput(mandatory.Id, null, Remove: true) }));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("mandatory_task");
    }

    // ── Tenant isolation (NFR-2) ────────────────────────────────────────

    [Fact]
    public async Task Other_tenant_cannot_read_instance()
    {
        SeedEmployees();
        var templateId = SeedTemplate();
        var instance = (await Service().AssignAsync(Assign(templateId))).Value!;

        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(Guid.NewGuid());
        ctxB.IsResolved.Returns(true);
        var svcB = new OnboardingChecklistService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _user,
            Substitute.For<ILogger<OnboardingChecklistService>>());

        var read = await svcB.GetInstanceAsync(instance.Id);

        read.IsFailure.Should().BeTrue();
        read.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Unresolved_tenant_is_refused()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var svc = new OnboardingChecklistService(
            TestDbContextFactory.Create(ctx, _dbName), ctx, _user,
            Substitute.For<ILogger<OnboardingChecklistService>>());

        var result = await svc.AssignAsync(Assign(Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }
}
