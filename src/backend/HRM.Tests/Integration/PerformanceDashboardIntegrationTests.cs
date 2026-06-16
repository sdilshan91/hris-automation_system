// ============================================================================
// US-PRF-007: Performance dashboard + analytics — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters), covering:
//   - Overview averages + scored count over manager_review.final_score (AC-1/BR-4).
//   - Multi-tenant isolation: Tenant A's dashboard excludes Tenant B's employees/scores (NFR-2/AC-5).
//   - Manager scope through the pipeline: a manager sees only their direct reports (AC-5/BR-3).
//   - CSV + XLSX export through the ExportPerformanceDashboardQuery (FR-8/AC-4).
//
// NOTE ON PROVIDER: identical rationale to the other dashboard integration tests — the verify gate runs
// `dotnet test` with NO PostgreSQL bound and Docker is unavailable in the agent sandbox, so a
// Testcontainers test would red the gate. These tests use the InMemory provider through the real composed
// pipeline (MediatR + tenant query filters), which is what proves tenant + scope isolation.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Application.Features.Performance.Queries;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HRM.Tests.Integration;

public sealed class PerformanceDashboardIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _deptA = Guid.NewGuid();
    private readonly Guid _deptB = Guid.NewGuid();
    private readonly Guid _cycleA = Guid.NewGuid();
    private readonly Guid _cycleB = Guid.NewGuid();

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _managerEmpId = Guid.NewGuid();
    private readonly Guid _reportEmpId = Guid.NewGuid();
    private readonly Guid _otherEmpId = Guid.NewGuid();   // Tenant A, not a report of the manager

    public PerformanceDashboardIntegrationTests()
    {
        Seed();
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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; init; }
        public string Email => "hr@t.com";
        public Guid TenantId { get; init; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions { get; init; } = [];
        public bool IsAuthenticated => true;
    }

    private IMediator BuildPipeline(Guid tenantId, ICurrentUser user)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(user);
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IPerformanceDashboardService, PerformanceDashboardService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(PerformanceDashboardOverviewQuery).Assembly));

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMediator>();
    }

    private ICurrentUser Hr(Guid tenantId) => new FakeCurrentUser
    {
        UserId = Guid.NewGuid(), TenantId = tenantId,
        Permissions = new[] { PermissionCatalog.Performance.ViewAll },
    };

    private ICurrentUser Manager(Guid tenantId) => new FakeCurrentUser
    {
        UserId = _managerUserId, TenantId = tenantId,
        Permissions = new[] { PermissionCatalog.Performance.ViewTeam },
    };

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private void Seed()
    {
        var now = DateTime.UtcNow;

        using (var db = Db(_tenantA))
        {
            db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme" });
            db.Departments.Add(new Department { Id = _deptA, TenantId = _tenantA, Name = "Engineering" });

            db.Employees.Add(new Employee
            {
                Id = _managerEmpId, TenantId = _tenantA, UserId = _managerUserId, EmployeeNo = "A-MGR",
                FirstName = "Grace", LastName = "Hopper", Email = "g@a.com", DepartmentId = _deptA,
                Status = EmployeeStatus.Active, IsActive = true, EmploymentType = EmploymentType.FullTime,
            });
            db.Employees.Add(new Employee
            {
                Id = _reportEmpId, TenantId = _tenantA, EmployeeNo = "A-RPT", FirstName = "Ada",
                LastName = "Lovelace", Email = "a@a.com", DepartmentId = _deptA,
                ReportsToEmployeeId = _managerEmpId, Status = EmployeeStatus.Active, IsActive = true,
                EmploymentType = EmploymentType.FullTime,
            });
            db.Employees.Add(new Employee
            {
                Id = _otherEmpId, TenantId = _tenantA, EmployeeNo = "A-OTH", FirstName = "Alan",
                LastName = "Turing", Email = "t@a.com", DepartmentId = _deptA,
                ReportsToEmployeeId = Guid.NewGuid(), Status = EmployeeStatus.Active, IsActive = true,
                EmploymentType = EmploymentType.FullTime,
            });

            db.AppraisalCycles.Add(new AppraisalCycle
            {
                Id = _cycleA, TenantId = _tenantA, Name = "A-FY2026", Status = AppraisalCycleStatus.Active,
                StartDate = now.AddDays(-90), EndDate = now.AddDays(10), RatingScaleMax = 5,
            });

            // Report scored 4.0, other scored 2.0.
            db.ManagerReviews.Add(new ManagerReview
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, CycleId = _cycleA, EmployeeId = _reportEmpId,
                Status = ManagerReviewStatus.Submitted, FinalScore = 4.0m, SubmittedAt = now,
            });
            db.ManagerReviews.Add(new ManagerReview
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, CycleId = _cycleA, EmployeeId = _otherEmpId,
                Status = ManagerReviewStatus.Submitted, FinalScore = 2.0m, SubmittedAt = now,
            });

            db.SaveChanges();
        }

        using (var db = Db(_tenantB))
        {
            db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });
            db.Departments.Add(new Department { Id = _deptB, TenantId = _tenantB, Name = "Ops" });

            var bEmp = Guid.NewGuid();
            db.Employees.Add(new Employee
            {
                Id = bEmp, TenantId = _tenantB, EmployeeNo = "B-1", FirstName = "Bee", LastName = "X",
                Email = "b@b.com", DepartmentId = _deptB, Status = EmployeeStatus.Active, IsActive = true,
                EmploymentType = EmploymentType.FullTime,
            });
            db.AppraisalCycles.Add(new AppraisalCycle
            {
                Id = _cycleB, TenantId = _tenantB, Name = "B-FY2026", Status = AppraisalCycleStatus.Active,
                StartDate = now.AddDays(-90), EndDate = now.AddDays(10), RatingScaleMax = 5,
            });
            db.ManagerReviews.Add(new ManagerReview
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantB, CycleId = _cycleB, EmployeeId = bEmp,
                Status = ManagerReviewStatus.Submitted, FinalScore = 1.0m, SubmittedAt = now,
            });

            db.SaveChanges();
        }
    }

    private static PerformanceDashboardFilter Filter(Guid cycleId) => new() { CycleId = cycleId };

    [Fact]
    public async Task Hr_overview_averages_tenant_a_scores()
    {
        var mediator = BuildPipeline(_tenantA, Hr(_tenantA));
        var result = await mediator.Send(new PerformanceDashboardOverviewQuery(Filter(_cycleA)));

        result.IsSuccess.Should().BeTrue(result.Error);
        var d = result.Value!;
        d.ScoredEmployeeCount.Should().Be(2);          // report 4.0 + other 2.0
        d.AverageScore.Should().Be(3.0m);
    }

    [Fact]
    public async Task Tenant_a_dashboard_excludes_tenant_b()
    {
        var mediator = BuildPipeline(_tenantA, Hr(_tenantA));
        var d = (await mediator.Send(new PerformanceDashboardOverviewQuery(Filter(_cycleA)))).Value!;

        // Tenant B's single 1.0 score must never bleed in.
        d.ScoredEmployeeCount.Should().Be(2);
        d.TopPerformers.Should().OnlyContain(p => p.Score >= 2.0m);
    }

    [Fact]
    public async Task Tenant_b_sees_only_its_own_data()
    {
        var mediator = BuildPipeline(_tenantB, Hr(_tenantB));
        var d = (await mediator.Send(new PerformanceDashboardOverviewQuery(Filter(_cycleB)))).Value!;

        d.ScoredEmployeeCount.Should().Be(1);
        d.AverageScore.Should().Be(1.0m);
    }

    [Fact]
    public async Task Manager_pipeline_sees_only_direct_reports()
    {
        var mediator = BuildPipeline(_tenantA, Manager(_tenantA));
        var d = (await mediator.Send(new PerformanceDashboardOverviewQuery(Filter(_cycleA)))).Value!;

        d.Scope.Should().Be("Team");
        d.ScoredEmployeeCount.Should().Be(1);          // only the direct report (4.0), NOT the other (2.0)
        d.AverageScore.Should().Be(4.0m);
        d.TopPerformers.Should().ContainSingle(p => p.EmployeeId == _reportEmpId);
        d.BottomPerformers.Should().BeEmpty();         // BR-3: no org-wide bottom list for a manager
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task Export_produces_a_non_empty_file(string format)
    {
        var mediator = BuildPipeline(_tenantA, Hr(_tenantA));
        var result = await mediator.Send(new ExportPerformanceDashboardQuery(Filter(_cycleA), format));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.FileContent.Should().NotBeEmpty();
        result.Value!.FileName.Should().Contain("performance-dashboard");
    }
}
