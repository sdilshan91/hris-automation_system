// ============================================================================
// US-ONB-003: New-hire self-service onboarding tests (OnboardingChecklistService).
//
// Drives the service through the real EF Core InMemory provider (so instances, task instances, and outbox
// rows actually persist and the global query filter applies). Covers:
//   - AC-1/AC-2/FR-1/FR-4: GetMyChecklist groups by category + computes progress/overdue counts.
//   - AC-1/FR-4: GetMyChecklistProgress returns the cheap summary.
//   - AC-3/FR-2: complete an Employee task → status completed, timestamp + actor + comment recorded,
//     progress recalculated, HR + manager outbox rows written (FR-5).
//   - AC-4/FR-3/NFR-6: document-required task needs a file; the file is stored at the tenant-isolated path;
//     MIME + size validation; malware-scan seam invoked.
//   - FR-7/BR-1: employee cannot complete an IT/Manager/HR task (403) nor another employee's task (404).
//   - BR-3: a completed task cannot be re-completed (409).
//   - FR-6/AC-5/BR-4: the overdue sweep writes overdue rows to employee + HR + manager and is idempotent.
//   - NFR-2: tenant isolation on read + sweep.
// ============================================================================

using System.Text;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class OnboardingMyChecklistServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _employeeUser;

    private readonly Guid _hrUserId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _employeeUserId = Guid.NewGuid();
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();

    private readonly IFileStorage _fileStorage = Substitute.For<IFileStorage>();
    private readonly IVirusScanner _scanner = Substitute.For<IVirusScanner>();

    public OnboardingMyChecklistServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        // The CALLER is the new hire (self-service).
        _employeeUser = Substitute.For<ICurrentUser>();
        _employeeUser.UserId.Returns(_employeeUserId);
        _employeeUser.Email.Returns("nora@acme.com");
        _employeeUser.IsAuthenticated.Returns(true);

        _scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Clean());
        _fileStorage.UploadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Stream>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult((string)ci[1]));
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private OnboardingChecklistService Service() =>
        new(Db(), _tenantContext, _employeeUser, _fileStorage, _scanner,
            Substitute.For<ILogger<OnboardingChecklistService>>());

    private void SeedEmployees()
    {
        using var db = Db();
        db.Employees.AddRange(
            new Employee
            {
                Id = _managerId, TenantId = _tenantId, EmployeeNo = "EMP-0001",
                FirstName = "Mona", LastName = "Manager", Email = "mona@acme.com",
                DateOfJoining = DateTime.UtcNow.AddYears(-2), UserId = _managerUserId,
            },
            new Employee
            {
                Id = _employeeId, TenantId = _tenantId, EmployeeNo = "EMP-0002",
                FirstName = "Nora", LastName = "Newhire", Email = "nora@acme.com",
                DateOfJoining = DateTime.UtcNow.AddDays(-1).Date,
                UserId = _employeeUserId, ReportsToEmployeeId = _managerId,
            });
        db.SaveChanges();
    }

    /// <summary>Seeds an active checklist with the given tasks; returns the checklist + task ids by title.</summary>
    private (Guid ChecklistId, Dictionary<string, Guid> TaskIds) SeedChecklist(
        params (string Title, OnboardingResponsibleRole Role, bool Mandatory, bool RequiresDoc,
                OnboardingTaskStatus Status, int DueOffsetDays, string Category)[] tasks)
    {
        var checklistId = BaseEntity.NewUuidV7();
        var taskIds = new Dictionary<string, Guid>();
        using var db = Db();
        var instance = new OnboardingChecklistInstance
        {
            Id = checklistId, TenantId = _tenantId, EmployeeId = _employeeId,
            TemplateId = BaseEntity.NewUuidV7(), TemplateName = "Standard", Status = OnboardingChecklistStatus.Active,
            StartDate = DateTime.UtcNow.AddDays(-1).Date, Version = 1, AssignedByUserId = _hrUserId,
        };
        var sort = 0;
        foreach (var t in tasks)
        {
            var id = BaseEntity.NewUuidV7();
            taskIds[t.Title] = id;
            instance.Tasks.Add(new OnboardingTaskInstance
            {
                Id = id, TenantId = _tenantId, ChecklistInstanceId = checklistId,
                Title = t.Title, Category = t.Category, ResponsibleRole = t.Role,
                ResponsibleUserId = t.Role == OnboardingResponsibleRole.Employee ? _employeeUserId : null,
                DueDate = DateTime.UtcNow.AddDays(t.DueOffsetDays).Date, Status = t.Status,
                IsMandatory = t.Mandatory, RequiresDocument = t.RequiresDoc, SortOrder = sort++,
            });
        }
        db.OnboardingChecklistInstances.Add(instance);
        db.SaveChanges();
        return (checklistId, taskIds);
    }

    private static CompleteTaskInput Complete(Guid taskId, string? comment = null, Stream? file = null,
        string? fileName = null, string? contentType = null, long size = 0) =>
        new(taskId, comment, file, fileName, contentType, size);

    // ── AC-1/AC-2/FR-1/FR-4 ─────────────────────────────────────────────

    [Fact]
    public async Task GetMyChecklist_groups_by_category_and_computes_progress()
    {
        SeedEmployees();
        SeedChecklist(
            ("Read handbook", OnboardingResponsibleRole.Employee, true, false, OnboardingTaskStatus.Completed, 1, "Docs"),
            ("Submit ID", OnboardingResponsibleRole.Employee, true, true, OnboardingTaskStatus.Pending, 2, "Docs"),
            ("Provision laptop", OnboardingResponsibleRole.IT, false, false, OnboardingTaskStatus.Pending, -2, "IT"));

        var result = await Service().GetMyChecklistAsync();

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.TotalTasks.Should().Be(3);
        dto.CompletedTasks.Should().Be(1);
        dto.ProgressPercent.Should().Be(33); // 1/3
        dto.OverdueTasks.Should().Be(1);      // the IT task due 2 days ago, still pending
        dto.Categories.Should().HaveCount(2);
        dto.Categories.Single(c => c.Category == "Docs").Tasks.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetMyChecklistProgress_returns_summary()
    {
        SeedEmployees();
        SeedChecklist(
            ("A", OnboardingResponsibleRole.Employee, true, false, OnboardingTaskStatus.Completed, 1, "Docs"),
            ("B", OnboardingResponsibleRole.Employee, false, false, OnboardingTaskStatus.Pending, 1, "Docs"));

        var result = await Service().GetMyChecklistProgressAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value!.ProgressPercent.Should().Be(50);
        result.Value.CompletedTasks.Should().Be(1);
        result.Value.PendingTasks.Should().Be(1);
    }

    [Fact]
    public async Task GetMyChecklist_when_no_active_checklist_returns_404()
    {
        SeedEmployees();
        var result = await Service().GetMyChecklistAsync();
        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── AC-3/FR-2/FR-5 ──────────────────────────────────────────────────

    [Fact]
    public async Task CompleteTask_marks_completed_records_actor_and_recalculates_progress()
    {
        SeedEmployees();
        var (_, ids) = SeedChecklist(
            ("Read handbook", OnboardingResponsibleRole.Employee, false, false, OnboardingTaskStatus.Pending, 1, "Docs"),
            ("Set up payroll", OnboardingResponsibleRole.Employee, false, false, OnboardingTaskStatus.Pending, 1, "Docs"));

        var result = await Service().CompleteTaskAsync(Complete(ids["Read handbook"], "done!"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Task.Status.Should().Be("completed");
        result.Value.Task.CompletedBy.Should().Be(_employeeUserId);
        result.Value.Task.Comment.Should().Be("done!");
        result.Value.Progress.ProgressPercent.Should().Be(50);

        using var db = Db();
        var task = db.OnboardingTaskInstances.Single(t => t.Id == ids["Read handbook"]);
        task.Status.Should().Be(OnboardingTaskStatus.Completed);
        task.CompletedAt.Should().NotBeNull();

        // FR-5: HR + manager outbox rows written.
        var outbox = db.OnboardingNotificationOutbox
            .Where(o => o.NotificationType == "onboarding.task.completed").ToList();
        outbox.Select(o => o.RecipientUserId).Should().Contain(new[] { _hrUserId, _managerUserId });
    }

    // ── AC-4/FR-3/NFR-6 document upload ─────────────────────────────────

    [Fact]
    public async Task CompleteTask_stores_document_at_tenant_isolated_path()
    {
        SeedEmployees();
        var (_, ids) = SeedChecklist(
            ("Submit ID", OnboardingResponsibleRole.Employee, true, true, OnboardingTaskStatus.Pending, 1, "Docs"));

        using var file = new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 fake"));
        var result = await Service().CompleteTaskAsync(
            Complete(ids["Submit ID"], file: file, fileName: "id-proof.pdf", contentType: "application/pdf", size: file.Length));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Task.Status.Should().Be("completed");

        await _fileStorage.Received(1).UploadAsync(
            _tenantId,
            $"onboarding/{_employeeId}/{ids["Submit ID"]}/id-proof.pdf",
            Arg.Any<Stream>(), "application/pdf", Arg.Any<CancellationToken>());
        await _scanner.Received(1).ScanAsync(Arg.Any<Stream>(), "id-proof.pdf", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteTask_document_required_without_file_is_rejected()
    {
        SeedEmployees();
        var (_, ids) = SeedChecklist(
            ("Submit ID", OnboardingResponsibleRole.Employee, true, true, OnboardingTaskStatus.Pending, 1, "Docs"));

        var result = await Service().CompleteTaskAsync(Complete(ids["Submit ID"]));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
        result.ErrorCode.Should().Be("document_required");
    }

    [Fact]
    public async Task CompleteTask_rejects_disallowed_mime_type()
    {
        SeedEmployees();
        var (_, ids) = SeedChecklist(
            ("Submit ID", OnboardingResponsibleRole.Employee, true, true, OnboardingTaskStatus.Pending, 1, "Docs"));

        using var file = new MemoryStream(Encoding.UTF8.GetBytes("evil"));
        var result = await Service().CompleteTaskAsync(
            Complete(ids["Submit ID"], file: file, fileName: "x.exe", contentType: "application/x-msdownload", size: file.Length));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_file_type");
    }

    [Fact]
    public async Task CompleteTask_rejects_file_over_10mb()
    {
        SeedEmployees();
        var (_, ids) = SeedChecklist(
            ("Submit ID", OnboardingResponsibleRole.Employee, true, true, OnboardingTaskStatus.Pending, 1, "Docs"));

        using var file = new MemoryStream(new byte[16]);
        var result = await Service().CompleteTaskAsync(
            Complete(ids["Submit ID"], file: file, fileName: "big.pdf", contentType: "application/pdf",
                size: 11 * 1024 * 1024));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("file_too_large");
    }

    // ── FR-7/BR-1 role restriction ──────────────────────────────────────

    [Fact]
    public async Task CompleteTask_employee_cannot_complete_an_IT_task()
    {
        SeedEmployees();
        var (_, ids) = SeedChecklist(
            ("Provision laptop", OnboardingResponsibleRole.IT, false, false, OnboardingTaskStatus.Pending, 1, "IT"));

        var result = await Service().CompleteTaskAsync(Complete(ids["Provision laptop"]));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(403);
        result.ErrorCode.Should().Be("task_not_yours");
    }

    [Fact]
    public async Task CompleteTask_cannot_complete_another_employees_task()
    {
        SeedEmployees();
        // A second employee with their own checklist + employee task.
        var otherEmpId = BaseEntity.NewUuidV7();
        var otherChecklistId = BaseEntity.NewUuidV7();
        var otherTaskId = BaseEntity.NewUuidV7();
        using (var db = Db())
        {
            db.Employees.Add(new Employee
            {
                Id = otherEmpId, TenantId = _tenantId, EmployeeNo = "EMP-0003",
                FirstName = "Otto", LastName = "Other", Email = "otto@acme.com",
                DateOfJoining = DateTime.UtcNow.AddDays(-1).Date, UserId = Guid.NewGuid(),
            });
            db.OnboardingChecklistInstances.Add(new OnboardingChecklistInstance
            {
                Id = otherChecklistId, TenantId = _tenantId, EmployeeId = otherEmpId,
                TemplateId = BaseEntity.NewUuidV7(), TemplateName = "Standard",
                Status = OnboardingChecklistStatus.Active, StartDate = DateTime.UtcNow.Date,
                Tasks = { new OnboardingTaskInstance
                {
                    Id = otherTaskId, TenantId = _tenantId, ChecklistInstanceId = otherChecklistId,
                    Title = "Other task", ResponsibleRole = OnboardingResponsibleRole.Employee,
                    DueDate = DateTime.UtcNow.Date, Status = OnboardingTaskStatus.Pending,
                } },
            });
            db.SaveChanges();
        }

        // Caller is Nora (the first employee) — must not be able to complete Otto's task.
        var result = await Service().CompleteTaskAsync(Complete(otherTaskId));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404); // 404 to avoid leaking existence.
    }

    // ── BR-3 cannot revert ──────────────────────────────────────────────

    [Fact]
    public async Task CompleteTask_already_completed_is_rejected()
    {
        SeedEmployees();
        var (_, ids) = SeedChecklist(
            ("Read handbook", OnboardingResponsibleRole.Employee, false, false, OnboardingTaskStatus.Completed, 1, "Docs"));

        var result = await Service().CompleteTaskAsync(Complete(ids["Read handbook"]));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.ErrorCode.Should().Be("task_already_completed");
    }

    // ── FR-6/AC-5/BR-4 overdue sweep ────────────────────────────────────

    [Fact]
    public async Task OverdueSweep_writes_rows_to_employee_hr_and_manager()
    {
        SeedEmployees();
        SeedChecklist(
            ("Read handbook", OnboardingResponsibleRole.Employee, true, false, OnboardingTaskStatus.Pending, -3, "Docs"));

        var written = await Service().RunOverdueSweepAsync(_tenantId);

        written.Should().Be(3); // employee + HR + manager.
        using var db = Db();
        var rows = db.OnboardingNotificationOutbox
            .Where(o => o.NotificationType == "onboarding.task.overdue").ToList();
        rows.Select(o => o.RecipientUserId).Should().BeEquivalentTo(new[] { _employeeUserId, _hrUserId, _managerUserId });
    }

    [Fact]
    public async Task OverdueSweep_is_idempotent_within_the_same_day()
    {
        SeedEmployees();
        SeedChecklist(
            ("Read handbook", OnboardingResponsibleRole.Employee, true, false, OnboardingTaskStatus.Pending, -3, "Docs"));

        var first = await Service().RunOverdueSweepAsync(_tenantId);
        var second = await Service().RunOverdueSweepAsync(_tenantId);

        first.Should().Be(3);
        second.Should().Be(0); // already notified today → no new rows.
    }

    [Fact]
    public async Task OverdueSweep_ignores_completed_and_future_tasks()
    {
        SeedEmployees();
        SeedChecklist(
            ("Done", OnboardingResponsibleRole.Employee, true, false, OnboardingTaskStatus.Completed, -3, "Docs"),
            ("Future", OnboardingResponsibleRole.Employee, false, false, OnboardingTaskStatus.Pending, 5, "Docs"));

        var written = await Service().RunOverdueSweepAsync(_tenantId);

        written.Should().Be(0);
    }

    // ── NFR-2 tenant isolation ──────────────────────────────────────────

    [Fact]
    public async Task OverdueSweep_only_touches_the_passed_tenant()
    {
        SeedEmployees();
        SeedChecklist(
            ("Read handbook", OnboardingResponsibleRole.Employee, true, false, OnboardingTaskStatus.Pending, -3, "Docs"));

        var written = await Service().RunOverdueSweepAsync(Guid.NewGuid()); // a different tenant.

        written.Should().Be(0);
    }
}
