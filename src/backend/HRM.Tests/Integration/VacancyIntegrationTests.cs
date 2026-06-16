// ============================================================================
// US-REC-001: Create & Publish Job Vacancy — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters + the real Ganss HTML sanitizer), covering:
//   - Create draft -> publish -> internal list happy path (AC-1/AC-2, FR-5 slug, ref number).
//   - BR-2: publish is rejected when required fields are missing.
//   - FR-2: invalid status transitions are rejected; close keeps the row (AC-5).
//   - NFR-4: <script> in the description is stripped on persist.
//   - AC-4: two tenants — a cross-tenant query returns empty (EF global query filter isolation).
//   - Public careers (FR-4/BR-5): toggle off -> nothing public; toggle on -> only Open+public vacancies.
//
// NOTE ON PROVIDER: identical rationale to the Attendance integration tests — the verify gate runs
// `dotnet test` with NO PostgreSQL bound and Docker is unavailable in the agent sandbox, so a
// Testcontainers test would red the gate. These tests use the InMemory provider through the real
// composed pipeline (MediatR + tenant query filters), which is what proves tenant isolation (AC-4).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration;

public sealed class VacancyIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _deptEng = Guid.NewGuid();
    private readonly Guid _jobTitle = Guid.NewGuid();
    private readonly Guid _manager = Guid.NewGuid();

    public VacancyIntegrationTests()
    {
        SeedMasterData(_tenantA);
        SeedMasterData(_tenantB, new Guid("00000000-0000-0000-0000-0000000000bb"));
    }

    // ── Test doubles (mirroring the Attendance integration-test pattern) ──

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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; init; } = Guid.NewGuid();
        public string Email => "recruiter@t.com";
        public Guid TenantId { get; init; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
    }

    private IMediator BuildPipeline(Guid tenantId)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId });
        services.AddSingleton<IHtmlSanitizer, GanssHtmlSanitizer>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IVacancyService, VacancyService>();
        services.AddScoped<IPublicCareersService, PublicCareersService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(CreateVacancyCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private void SeedMasterData(Guid tenantId, Guid? deptId = null)
    {
        using var db = Db(tenantId);
        var d = deptId ?? _deptEng;
        var jt = deptId is null ? _jobTitle : Guid.NewGuid();
        var mgr = deptId is null ? _manager : Guid.NewGuid();

        db.Departments.Add(new Department { Id = d, TenantId = tenantId, Name = "Engineering", Code = "ENG" });
        db.JobTitles.Add(new JobTitle { Id = jt, TenantId = tenantId, TitleName = "Engineer", IsActive = true });
        db.Employees.Add(new Employee
        {
            Id = mgr, TenantId = tenantId,
            EmployeeNo = "MGR", FirstName = "Hiring", LastName = "Manager", Email = "mgr@t.com",
            DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = d, JobTitleId = jt,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        db.SaveChanges();
    }

    private void SetCareersToggle(Guid tenantId, bool enabled)
    {
        using var db = Db(tenantId);
        var tenant = db.Tenants.IgnoreQueryFilters().FirstOrDefault(t => t.Id == tenantId);
        if (tenant is null)
        {
            db.Tenants.Add(new Tenant
            {
                Id = tenantId, Name = "T", Subdomain = $"t{tenantId:N}".Substring(0, 8),
                Status = TenantStatus.Active, PublicCareersEnabled = enabled,
            });
        }
        else
        {
            tenant.PublicCareersEnabled = enabled;
        }
        db.SaveChanges();
    }

    private CreateVacancyCommand FullDraft(string title = "Senior Software Engineer") => new(
        Title: title,
        DepartmentId: _deptEng,
        JobTitleId: _jobTitle,
        LocationId: null,
        HiringManagerId: _manager,
        EmploymentType: EmploymentType.FullTime,
        Headcount: 2,
        SalaryMin: null, SalaryMax: null, SalaryCurrency: null,
        Description: "<p>Build great software.</p>",
        Qualifications: "<p>5 years experience.</p>",
        ApplicationDeadline: null,
        PublishToPublicCareers: true);

    // ════════════════════════════════════════════════════════════════
    //  Create -> publish -> list happy path (AC-1/AC-2, FR-5)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CreatePublishList_HappyPath()
    {
        var mediator = BuildPipeline(_tenantA);

        var created = await mediator.Send(FullDraft());
        created.IsSuccess.Should().BeTrue();
        created.Value!.Status.Should().Be(VacancyStatus.Draft);
        created.Value.ReferenceNumber.Should().StartWith("VAC-");
        created.Value.Slug.Should().BeNull();   // no slug until published

        var published = await mediator.Send(new PublishVacancyCommand(created.Value.Id));
        published.IsSuccess.Should().BeTrue();
        published.Value!.Status.Should().Be(VacancyStatus.Open);
        published.Value.Slug.Should().Be("senior-software-engineer");
        published.Value.PublishedAt.Should().NotBeNull();

        var list = await mediator.Send(new ListVacanciesQuery(VacancyStatus.Open, null));
        list.IsSuccess.Should().BeTrue();
        list.Value!.TotalCount.Should().Be(1);
        list.Value.Items.Single().Id.Should().Be(created.Value.Id);
        // Display-name companions are projected (no N+1 explosion).
        list.Value.Items.Single().DepartmentName.Should().Be("Engineering");
        list.Value.Items.Single().HiringManagerName.Should().Be("Hiring Manager");
    }

    [Fact]
    public async Task ReferenceNumbers_AreSequentialPerTenantYear()
    {
        var mediator = BuildPipeline(_tenantA);

        var first = await mediator.Send(FullDraft("First"));
        var second = await mediator.Send(FullDraft("Second"));

        var year = DateTime.UtcNow.Year;
        first.Value!.ReferenceNumber.Should().Be($"VAC-{year}-0001");
        second.Value!.ReferenceNumber.Should().Be($"VAC-{year}-0002");
    }

    [Fact]
    public async Task Publish_GeneratesUniqueSlug_OnCollision()
    {
        var mediator = BuildPipeline(_tenantA);

        var a = await mediator.Send(FullDraft("Same Title"));
        var b = await mediator.Send(FullDraft("Same Title"));
        await mediator.Send(new PublishVacancyCommand(a.Value!.Id));
        var bPublished = await mediator.Send(new PublishVacancyCommand(b.Value!.Id));

        bPublished.Value!.Slug.Should().Be("same-title-2");
    }

    // ════════════════════════════════════════════════════════════════
    //  BR-2: publish requires the required fields
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Publish_Rejected_WhenRequiredFieldsMissing()
    {
        var mediator = BuildPipeline(_tenantA);

        // A draft missing department, job title, and hiring manager.
        var draft = await mediator.Send(new CreateVacancyCommand(
            "Incomplete", null, null, null, null,
            EmploymentType.FullTime, 1, null, null, null,
            "<p>desc</p>", null, null, true));
        draft.IsSuccess.Should().BeTrue();

        var publish = await mediator.Send(new PublishVacancyCommand(draft.Value!.Id));
        publish.IsFailure.Should().BeTrue();
        publish.ErrorCode.Should().Be("publish_requirements");
        publish.Error.Should().Contain("department");
    }

    // ════════════════════════════════════════════════════════════════
    //  FR-2 transitions + AC-5 close
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Close_OpenVacancy_KeepsRowAndStampsClosedAt()
    {
        var mediator = BuildPipeline(_tenantA);
        var created = await mediator.Send(FullDraft());
        await mediator.Send(new PublishVacancyCommand(created.Value!.Id));

        var closed = await mediator.Send(new CloseVacancyCommand(created.Value.Id));
        closed.IsSuccess.Should().BeTrue();
        closed.Value!.Status.Should().Be(VacancyStatus.Closed);
        closed.Value.ClosedAt.Should().NotBeNull();

        // BR-3: the vacancy still exists after close.
        var fetched = await mediator.Send(new GetVacancyByIdQuery(created.Value.Id));
        fetched.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ChangeStatus_DraftToClosed_IsRejected()
    {
        var mediator = BuildPipeline(_tenantA);
        var created = await mediator.Send(FullDraft());

        var result = await mediator.Send(
            new ChangeVacancyStatusCommand(created.Value!.Id, VacancyStatus.Closed));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_transition");
    }

    [Fact]
    public async Task ChangeStatus_DraftToOpen_RoutesThroughPublish()
    {
        var mediator = BuildPipeline(_tenantA);
        var created = await mediator.Send(FullDraft());

        var result = await mediator.Send(
            new ChangeVacancyStatusCommand(created.Value!.Id, VacancyStatus.Open));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("use_publish");
    }

    // ════════════════════════════════════════════════════════════════
    //  NFR-4: HTML sanitization on persist
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Create_StripsScriptTags_FromDescription()
    {
        var mediator = BuildPipeline(_tenantA);

        var created = await mediator.Send(FullDraft() with
        {
            Description = "<p>Hello</p><script>alert('xss')</script>",
        });

        created.IsSuccess.Should().BeTrue();
        created.Value!.Description.Should().NotContain("<script");
        created.Value.Description.Should().NotContain("alert");
        created.Value.Description.Should().Contain("Hello");
    }

    // ════════════════════════════════════════════════════════════════
    //  AC-4: cross-tenant isolation
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TenantB_CannotSeeTenantAVacancies()
    {
        var mediatorA = BuildPipeline(_tenantA);
        var created = await mediatorA.Send(FullDraft("Tenant A Only"));
        created.IsSuccess.Should().BeTrue();

        var mediatorB = BuildPipeline(_tenantB);

        // List for tenant B is empty.
        var listB = await mediatorB.Send(new ListVacanciesQuery(null, null));
        listB.Value!.TotalCount.Should().Be(0);

        // Direct fetch of tenant A's id from tenant B returns not-found (filtered out).
        var getB = await mediatorB.Send(new GetVacancyByIdQuery(created.Value!.Id));
        getB.IsFailure.Should().BeTrue();
        getB.StatusCode.Should().Be(404);
    }

    // ════════════════════════════════════════════════════════════════
    //  Public careers (FR-4/BR-5)
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PublicCareers_TogglOff_ReturnsEmpty()
    {
        var mediator = BuildPipeline(_tenantA);
        var created = await mediator.Send(FullDraft());
        await mediator.Send(new PublishVacancyCommand(created.Value!.Id));
        SetCareersToggle(_tenantA, enabled: false);

        var list = await mediator.Send(new ListPublicVacanciesQuery());
        list.IsSuccess.Should().BeTrue();
        list.Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task PublicCareers_ToggleOn_ListsAndFetchesBySlug()
    {
        var mediator = BuildPipeline(_tenantA);
        var created = await mediator.Send(FullDraft("Public Role"));
        await mediator.Send(new PublishVacancyCommand(created.Value!.Id));
        SetCareersToggle(_tenantA, enabled: true);

        var list = await mediator.Send(new ListPublicVacanciesQuery());
        list.Value!.Should().ContainSingle(v => v.Slug == "public-role");

        var detail = await mediator.Send(new GetPublicVacancyBySlugQuery("public-role"));
        detail.IsSuccess.Should().BeTrue();
        detail.Value!.Title.Should().Be("Public Role");
    }

    [Fact]
    public async Task PublicCareers_DraftVacancy_IsNeverExposed()
    {
        var mediator = BuildPipeline(_tenantA);
        await mediator.Send(FullDraft("Hidden Draft"));   // never published
        SetCareersToggle(_tenantA, enabled: true);

        var list = await mediator.Send(new ListPublicVacanciesQuery());
        list.Value!.Should().BeEmpty();
    }
}
