// ============================================================================
// US-ONB-004: Asset issuance integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (with the MediatR
// pipeline, the FluentValidation ValidationBehavior, and the global query filters), covering:
//   - Happy path: single + bulk issuance flips status to Assigned, links the employee, completes the linked
//     onboarding task, all in one transaction (AC-2/FR-4/NFR-5/FR-7).
//   - Double-assignment rejection with holder name (AC-3).
//   - Unique asset_tag rejection (BR-3).
//   - Available-status gate (FR-3/BR-1).
//   - Employee read-only own-assets view (AC-4/BR-6).
//   - Tenant isolation: Tenant B cannot see Tenant A's assets (AC-5/NFR-2).
//
// NOTE ON PROVIDER: mirrors the other module integration tests — EF Core InMemory through the real composed
// pipeline (no PostgreSQL, no Docker in the verify gate). The PG-specific schema (enum-as-string, unique
// index) is validated by the `migrations` CI job applying the generated migration to real PG.
// ============================================================================

using FluentAssertions;
using FluentValidation;
using HRM.Application.Common.Behaviors;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Onboarding.Commands;
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

public sealed class AssetIssuanceIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _hrUserA = Guid.NewGuid();

    private readonly Guid _employeeA = Guid.NewGuid();
    private readonly Guid _employeeUserA = Guid.NewGuid();
    private readonly Guid _otherEmployeeA = Guid.NewGuid();

    public AssetIssuanceIntegrationTests()
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

        services.AddSingleton(Substitute.For<IFileStorage>());
        var scanner = Substitute.For<IVirusScanner>();
        scanner.ScanAsync(Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(VirusScanResult.Clean());
        services.AddSingleton(scanner);

        services.AddScoped<IAssetService, AssetService>();

        services.AddValidatorsFromAssembly(typeof(IssueAssetsCommand).Assembly);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(IssueAssetsCommand).Assembly);
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
                Id = _employeeA, TenantId = _tenantA, EmployeeNo = "EMP-0001",
                FirstName = "Nora", LastName = "Newhire", Email = "nora@acme.com",
                DateOfJoining = DateTime.UtcNow.AddDays(-1).Date, UserId = _employeeUserA,
            },
            new Employee
            {
                Id = _otherEmployeeA, TenantId = _tenantA, EmployeeNo = "EMP-0002",
                FirstName = "Otto", LastName = "Other", Email = "otto@acme.com",
                DateOfJoining = DateTime.UtcNow.AddDays(-30).Date, UserId = Guid.NewGuid(),
            });

        db.SaveChanges();
    }

    private void SeedAsset(Guid tenantId, Guid id, string tag, AssetStatus status,
        string type = "Laptop", string? serial = null, Guid? assignedTo = null)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);
        db.Assets.Add(new Asset
        {
            Id = id, TenantId = tenantId, AssetType = type, AssetTag = tag, SerialNumber = serial,
            Status = status, Condition = AssetCondition.Good, AssignedEmployeeId = assignedTo,
        });
        db.SaveChanges();
    }

    private static IssueAssetCommandLine Line(string tag, string type = "Laptop", Guid? assetId = null,
        string? serial = null, AssetCondition condition = AssetCondition.New) =>
        new(assetId, type, tag, serial, null, null, condition, DateTime.UtcNow.Date, null);

    private static IssueAssetsCommand IssueCmd(Guid employeeId, Guid? taskId, params IssueAssetCommandLine[] lines) =>
        new(employeeId, taskId, lines, null, null, null, 0);

    // ── Happy path single + bulk (AC-2/FR-4/NFR-5/FR-7) ─────────────────

    [Fact]
    public async Task Issue_bulk_assigns_all_and_links_employee_in_one_transaction()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);

        var result = await mediator.Send(IssueCmd(_employeeA, null,
            Line("LAP-1"), Line("ID-1", "ID Card")));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().HaveCount(2);
        result.Value.Should().OnlyContain(a => a.Status == "Assigned" && a.AssignedEmployeeId == _employeeA);

        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        await using var db = new AppDbContext(options, ctx);
        var assigned = await db.Assets.Where(a => a.AssignedEmployeeId == _employeeA).ToListAsync();
        assigned.Should().HaveCount(2);
        assigned.Should().OnlyContain(a => a.TenantId == _tenantA);
    }

    [Fact]
    public async Task Issue_with_linked_task_completes_it_atomically()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);

        var checklistId = BaseEntity.NewUuidV7();
        var taskId = BaseEntity.NewUuidV7();
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using (var db = new AppDbContext(options, ctx))
        {
            db.OnboardingChecklistInstances.Add(new OnboardingChecklistInstance
            {
                Id = checklistId, TenantId = _tenantA, EmployeeId = _employeeA,
                TemplateId = BaseEntity.NewUuidV7(), TemplateName = "Standard",
                Status = OnboardingChecklistStatus.Active, StartDate = DateTime.UtcNow.Date,
                Tasks = { new OnboardingTaskInstance
                {
                    Id = taskId, TenantId = _tenantA, ChecklistInstanceId = checklistId,
                    Title = "Issue laptop", ResponsibleRole = OnboardingResponsibleRole.HR,
                    DueDate = DateTime.UtcNow.Date, Status = OnboardingTaskStatus.Pending,
                } },
            });
            db.SaveChanges();
        }

        var result = await mediator.Send(IssueCmd(_employeeA, taskId, Line("LAP-T")));

        result.IsSuccess.Should().BeTrue();
        await using var verify = new AppDbContext(options, ctx);
        var task = await verify.OnboardingTaskInstances.FirstAsync(t => t.Id == taskId);
        task.Status.Should().Be(OnboardingTaskStatus.Completed);
    }

    // ── AC-3 double assignment ──────────────────────────────────────────

    [Fact]
    public async Task Issue_already_assigned_asset_is_rejected_with_holder_name()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        var assetId = BaseEntity.NewUuidV7();
        SeedAsset(_tenantA, assetId, "LAP-A", AssetStatus.Assigned, serial: "SN-1", assignedTo: _otherEmployeeA);

        var result = await mediator.Send(IssueCmd(_employeeA, null,
            Line("LAP-A", assetId: assetId, serial: "SN-1")));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(409);
        result.Error.Should().Contain("Otto Other");
    }

    // ── BR-3 unique tag ─────────────────────────────────────────────────

    [Fact]
    public async Task Issue_adhoc_with_existing_tag_is_rejected()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        SeedAsset(_tenantA, BaseEntity.NewUuidV7(), "LAP-DUP", AssetStatus.Available);

        var result = await mediator.Send(IssueCmd(_employeeA, null, Line("LAP-DUP")));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("duplicate_asset");
    }

    // ── FR-3 available gate ─────────────────────────────────────────────

    [Fact]
    public async Task Available_lists_only_available_assets()
    {
        var mediator = BuildPipeline(_tenantA, _hrUserA);
        SeedAsset(_tenantA, BaseEntity.NewUuidV7(), "AVL-1", AssetStatus.Available);
        SeedAsset(_tenantA, BaseEntity.NewUuidV7(), "ASG-1", AssetStatus.Assigned, assignedTo: _otherEmployeeA);

        var result = await mediator.Send(new GetAvailableAssetsQuery(null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(a => a.AssetTag == "AVL-1");
    }

    // ── AC-4 employee read-only own view ────────────────────────────────

    [Fact]
    public async Task Employee_sees_own_assigned_assets()
    {
        var hr = BuildPipeline(_tenantA, _hrUserA);
        await hr.Send(IssueCmd(_employeeA, null, Line("LAP-MINE")));

        // The new hire queries their own assets via /me (resolved from the JWT user id).
        var employee = BuildPipeline(_tenantA, _employeeUserA);
        var result = await employee.Send(new GetMyAssetsQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(a => a.AssetTag == "LAP-MINE" && a.AssignedEmployeeId == _employeeA);
    }

    // ── AC-5 / NFR-2 tenant isolation ───────────────────────────────────

    [Fact]
    public async Task Tenant_B_cannot_see_tenant_A_assets()
    {
        var hrA = BuildPipeline(_tenantA, _hrUserA);
        await hrA.Send(IssueCmd(_employeeA, null, Line("LAP-A1")));

        var hrB = BuildPipeline(_tenantB, Guid.NewGuid());
        var result = await hrB.Send(new GetAvailableAssetsQuery(null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().NotContain(a => a.AssetTag == "LAP-A1");
    }
}
