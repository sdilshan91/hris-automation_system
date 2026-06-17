// ============================================================================
// US-ONB-002: Onboarding checklist assignment integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (with the MediatR
// pipeline, the FluentValidation ValidationBehavior, and the global query filters), covering:
//   - Happy path: assign persists the instance + task instances with the session tenant_id (FR-7),
//     calculated due dates (FR-2), pending status (AC-2), and writes outbox rows (NFR-3).
//   - Auto-filter: applicable-templates returns dept/universal matches, active only (AC-1/FR-1/BR-1).
//   - Replace/merge (AC-3/BR-2).
//   - Past joining-date recalculation (BR-4).
//   - Mandatory-not-removable on modify (BR-3).
//   - Tenant isolation: Tenant B cannot read Tenant A's checklist (NFR-2).
//
// NOTE ON PROVIDER: mirrors the other module integration tests — EF Core InMemory through the real
// composed pipeline (no PostgreSQL, no Docker in the verify gate). The PG-specific schema (jsonb column,
// enum-as-string) is validated by the `migrations` CI job applying the generated migration to real PG.
// The IBackgroundJobClient is intentionally NOT registered: the service takes it as an optional dependency
// so enqueue is a no-op here while the outbox rows (the durable part of NFR-3) are still written + asserted.
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

public sealed class OnboardingChecklistIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _hrUserA = Guid.NewGuid();
    private readonly Guid _hrUserB = Guid.NewGuid();

    private readonly Guid _deptId = Guid.NewGuid();
    private readonly Guid _jobTitleId = Guid.NewGuid();
    private readonly Guid _employeeA = Guid.NewGuid();
    private readonly Guid _employeeUserA = Guid.NewGuid();
    private readonly Guid _managerA = Guid.NewGuid();
    private readonly Guid _managerUserA = Guid.NewGuid();

    public OnboardingChecklistIntegrationTests()
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

    private IMediator BuildPipeline(Guid tenantId, Guid userId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(userId);
        currentUser.Email.Returns("hr@test.com");
        currentUser.IsAuthenticated.Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        // IBackgroundJobClient intentionally not registered (optional dependency) — enqueue is a no-op.
        services.AddScoped<IOnboardingChecklistService, OnboardingChecklistService>();

        services.AddValidatorsFromAssembly(typeof(AssignChecklistCommand).Assembly);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(AssignChecklistCommand).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedTenantsAndEmployees()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        db.Tenants.AddRange(
            new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme Corp" },
            new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });

        db.Employees.AddRange(
            new Employee
            {
                Id = _managerA, TenantId = _tenantA, EmployeeNo = "EMP-0001",
                FirstName = "Mona", LastName = "Manager", Email = "mona@acme.com",
                DepartmentId = _deptId, JobTitleId = _jobTitleId, DateOfJoining = DateTime.UtcNow.AddYears(-2),
                UserId = _managerUserA,
            },
            new Employee
            {
                Id = _employeeA, TenantId = _tenantA, EmployeeNo = "EMP-0002",
                FirstName = "Nora", LastName = "Newhire", Email = "nora@acme.com",
                DepartmentId = _deptId, JobTitleId = _jobTitleId, DateOfJoining = DateTime.UtcNow.AddDays(10).Date,
                UserId = _employeeUserA, ReportsToEmployeeId = _managerA,
            });

        db.SaveChanges();
    }

    private Guid SeedTemplate(
        Guid tenantId, bool isActive = true, IReadOnlyList<Guid>? depts = null, string name = "Standard Onboarding")
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        var templateId = BaseEntity.NewUuidV7();
        db.OnboardingChecklistTemplates.Add(new OnboardingChecklistTemplate
        {
            Id = templateId, TenantId = tenantId, TemplateName = name, IsActive = isActive,
            ApplicableDepartments = (depts ?? new List<Guid>()).ToList(),
            ApplicableJobTitles = new List<Guid>(),
            Tasks = new List<OnboardingTemplateTask>
            {
                new()
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TemplateId = templateId,
                    Title = "Sign contract", ResponsibleRole = OnboardingResponsibleRole.HR,
                    DueOffsetDays = 0, IsMandatory = true, SortOrder = 0,
                },
                new()
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = tenantId, TemplateId = templateId,
                    Title = "Provision laptop", ResponsibleRole = OnboardingResponsibleRole.IT,
                    DueOffsetDays = 2, IsMandatory = false, SortOrder = 1,
                },
            },
        });
        db.SaveChanges();
        return templateId;
    }

    private static AssignChecklistCommand AssignCmd(
        Guid employeeId, Guid templateId, ChecklistAssignmentMode mode = ChecklistAssignmentMode.Replace) =>
        new(employeeId, templateId, null, mode, Array.Empty<AssignChecklistAdHocTask>(), null);

    // ── Happy path (AC-2 / FR-2 / FR-7 / NFR-3) ─────────────────────────

    [Fact]
    public async Task Assign_persists_instance_with_due_dates_pending_tenant_and_outbox()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        var templateId = SeedTemplate(_tenantA);

        var result = await mediator.Send(AssignCmd(_employeeA, templateId));

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        var joining = DateTime.UtcNow.AddDays(10).Date;
        dto.TaskCount.Should().Be(2);
        dto.Tasks.Should().OnlyContain(t => t.Status == OnboardingTaskStatus.Pending);
        dto.Tasks.Single(t => t.Title == "Provision laptop").DueDate.Should().Be(joining.AddDays(2));

        var read = await mediator.Send(new GetChecklistInstanceQuery(dto.Id));
        read.IsSuccess.Should().BeTrue();
        read.Value!.Tasks.Should().HaveCount(2);

        // NFR-3 outbox rows written in the same transaction.
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        await using var db = new AppDbContext(options, ctx);
        var outbox = await db.OnboardingNotificationOutbox.Where(o => o.ChecklistInstanceId == dto.Id).ToListAsync();
        outbox.Should().NotBeEmpty();
        outbox.Should().OnlyContain(o => o.Status == OnboardingNotificationStatus.Pending && o.TenantId == _tenantA);
    }

    // ── AC-1 / FR-1 / BR-1 auto-filter ──────────────────────────────────

    [Fact]
    public async Task ApplicableTemplates_returns_universal_and_dept_matches_active_only()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        var universalId = SeedTemplate(_tenantA, name: "Universal");
        var deptId = SeedTemplate(_tenantA, depts: new[] { _deptId }, name: "Dept Match");
        var otherId = SeedTemplate(_tenantA, depts: new[] { Guid.NewGuid() }, name: "Other Dept");
        var inactiveId = SeedTemplate(_tenantA, isActive: false, name: "Inactive");

        var result = await mediator.Send(new GetApplicableTemplatesQuery(_employeeA));

        result.IsSuccess.Should().BeTrue();
        var ids = result.Value!.Select(t => t.Id).ToList();
        ids.Should().Contain(new[] { universalId, deptId });
        ids.Should().NotContain(new[] { otherId, inactiveId });
    }

    // ── AC-3 / BR-2 replace + merge ─────────────────────────────────────

    [Fact]
    public async Task Assign_replace_then_merge_behaviour()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        var templateId = SeedTemplate(_tenantA);

        var first = (await mediator.Send(AssignCmd(_employeeA, templateId))).Value!;
        var replaced = (await mediator.Send(AssignCmd(_employeeA, templateId, ChecklistAssignmentMode.Replace))).Value!;
        replaced.Id.Should().NotBe(first.Id);
        replaced.Version.Should().Be(2);

        var merged = (await mediator.Send(AssignCmd(_employeeA, templateId, ChecklistAssignmentMode.Merge))).Value!;
        merged.Id.Should().Be(replaced.Id);
        merged.TaskCount.Should().Be(4);
    }

    // ── BR-4 past joining date ──────────────────────────────────────────

    [Fact]
    public async Task Assign_past_joining_date_recalculates_from_today()
    {
        // Update the employee's joining date to the past.
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        await using (var db = new AppDbContext(options, ctx))
        {
            var emp = await db.Employees.FirstAsync(e => e.Id == _employeeA);
            emp.DateOfJoining = DateTime.UtcNow.AddDays(-10).Date;
            await db.SaveChangesAsync();
        }

        var mediator = BuildPipeline(_tenantA, _hrUserA);
        var templateId = SeedTemplate(_tenantA);

        var dto = (await mediator.Send(AssignCmd(_employeeA, templateId))).Value!;

        var today = DateTime.UtcNow.Date;
        dto.StartDate.Should().Be(today);
        dto.Tasks.Single(t => t.Title == "Provision laptop").DueDate.Should().Be(today.AddDays(2));
    }

    // ── BR-3 mandatory-not-removable ────────────────────────────────────

    [Fact]
    public async Task Modify_cannot_remove_mandatory_task()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        var templateId = SeedTemplate(_tenantA);
        var instance = (await mediator.Send(AssignCmd(_employeeA, templateId))).Value!;
        var mandatory = instance.Tasks.Single(t => t.Title == "Sign contract");

        var result = await mediator.Send(new ModifyAssignedChecklistCommand(
            instance.Id, Array.Empty<AssignChecklistAdHocTask>(),
            new[] { new ModifyChecklistTaskChange(mandatory.Id, null, true) }));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("mandatory_task");
    }

    // ── NFR-2 tenant isolation ──────────────────────────────────────────

    [Fact]
    public async Task Tenant_b_cannot_read_tenant_a_checklist()
    {
        var mediatorA = BuildPipeline(_tenantA, _hrUserA);
        var templateId = SeedTemplate(_tenantA);
        var instance = (await mediatorA.Send(AssignCmd(_employeeA, templateId))).Value!;

        var mediatorB = BuildPipeline(_tenantB, _hrUserB);
        var read = await mediatorB.Send(new GetChecklistInstanceQuery(instance.Id));

        read.IsFailure.Should().BeTrue();
        read.StatusCode.Should().Be(404);
    }
}
