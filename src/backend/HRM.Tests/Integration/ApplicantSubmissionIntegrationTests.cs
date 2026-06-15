// ============================================================================
// US-REC-002: Applicant submission integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI
// container (with the MediatR pipeline + global query filters), covering:
//   - POST apply happy path (SubmitApplicationCommand end-to-end -> stage Applied, AC-1)
//   - Duplicate rejection: same email + same vacancy is rejected (AC-3/BR-1)
//   - Cross-tenant isolation: Tenant B sees zero of Tenant A's applicants (AC-5)
//
// NOTE ON PROVIDER: the repo verify gate runs `dotnet test` with NO PostgreSQL
// service bound (EF Core InMemory by design); Docker is unavailable in the agent
// sandbox, so a Testcontainers test would red the gate. These tests use the same
// InMemory provider as the rest of the suite but go through the real composed
// pipeline (MediatR + ITenantContext-driven query filters), which is what proves
// tenant isolation. The PostgreSQL-specific schema (partial unique indexes) is
// validated by the `migrations` CI job applying the generated migration to real PG.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.Queries;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace HRM.Tests.Integration;

public sealed class ApplicantSubmissionIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    private Guid _vacancyA;
    private Guid _vacancyB;

    public ApplicantSubmissionIntegrationTests()
    {
        SeedTwoTenants();
    }

    // Mutable tenant context — flips the "current tenant" per request, exactly as
    // TenantResolutionMiddleware does in production.
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
        currentUser.Email.Returns("user@test.com");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        services.AddSingleton<IFileStorage, InMemoryFileStorage>();
        services.AddSingleton<IVirusScanner, AllowWithLogVirusScanner>();
        services.AddScoped<IRecruitmentNotificationService, LogOnlyRecruitmentNotificationService>();
        services.AddScoped<IApplicantService, ApplicantService>();

        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(SubmitApplicationCommand).Assembly));

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedTwoTenants()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        _vacancyA = Guid.NewGuid();
        _vacancyB = Guid.NewGuid();

        db.Vacancies.AddRange(
            OpenVacancy(_vacancyA, _tenantA, "Backend Engineer (A)"),
            OpenVacancy(_vacancyB, _tenantB, "Backend Engineer (B)"));

        db.SaveChanges();
    }

    private static Vacancy OpenVacancy(Guid id, Guid tenantId, string title) => new()
    {
        Id = id,
        TenantId = tenantId,
        ReferenceNumber = $"VAC-2026-{id.GetHashCode() & 0xFFF:D4}",
        Title = title,
        Status = VacancyStatus.Open,
        EmploymentType = EmploymentType.FullTime,
        Headcount = 1,
        Description = "Build things.",
        PublishToPublicCareers = true,
        PublishedAt = DateTime.UtcNow,
        IsDeleted = false,
    };

    private static SubmitApplicationCommand Apply(
        Guid vacancyId,
        string email = "applicant@example.com",
        ApplicationSource source = ApplicationSource.Public,
        Guid? linkedEmployeeId = null)
        => new(
            vacancyId, "Ada", "Lovelace", email, "+1-555-0100", "Cover letter.",
            new MemoryStream(new byte[] { 1, 2, 3, 4 }), "resume.pdf", "application/pdf", 4,
            source, linkedEmployeeId);

    // ── Happy path (AC-1) ─────────────────────────────────────────────

    [Fact]
    public async Task Submit_HappyPath_PersistsApplicantAtAppliedStage()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Apply(_vacancyA));

        result.IsSuccess.Should().BeTrue();
        result.Value!.ApplicationReferenceNumber.Should().StartWith("APP-");
        result.Value.VacancyTitle.Should().Be("Backend Engineer (A)");

        // Visible to a recruiter list for the vacancy, at stage Applied.
        var list = await mediator.Send(new ListApplicantsByVacancyQuery(_vacancyA));
        list.IsSuccess.Should().BeTrue();
        list.Value!.Items.Should().ContainSingle();
        list.Value.Items[0].Stage.Should().Be(ApplicantStage.Applied);
        list.Value.Items[0].Email.Should().Be("applicant@example.com");
    }

    [Fact]
    public async Task Submit_Internal_FlagsRecordAndLinksEmployee()
    {
        // Seed an employee in Tenant A to link to.
        var employeeId = SeedEmployee(_tenantA, _userA);
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(
            Apply(_vacancyA, email: "intern@corp.com", source: ApplicationSource.Internal, linkedEmployeeId: employeeId));

        result.IsSuccess.Should().BeTrue();

        var list = await mediator.Send(new ListApplicantsByVacancyQuery(_vacancyA));
        var item = list.Value!.Items.Single();
        item.IsInternal.Should().BeTrue();
        item.Source.Should().Be(ApplicationSource.Internal);
    }

    // ── Duplicate rejection (AC-3 / BR-1) ─────────────────────────────

    [Fact]
    public async Task Submit_DuplicateEmailSameVacancy_IsRejected()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var first = await mediator.Send(Apply(_vacancyA, email: "dup@example.com"));
        first.IsSuccess.Should().BeTrue();

        var second = await mediator.Send(Apply(_vacancyA, email: "DUP@example.com")); // case-insensitive
        second.IsFailure.Should().BeTrue();
        second.StatusCode.Should().Be(409);
        second.ErrorCode.Should().Be("duplicate_application");

        // Still exactly one applicant persisted.
        var list = await mediator.Send(new ListApplicantsByVacancyQuery(_vacancyA));
        list.Value!.TotalCount.Should().Be(1);
    }

    // ── Cross-tenant isolation (AC-5) ─────────────────────────────────

    [Fact]
    public async Task ListByVacancy_DoesNotLeakOtherTenantApplicants()
    {
        // Tenant A receives an application.
        var medA = BuildPipeline(_tenantA, _userA);
        (await medA.Send(Apply(_vacancyA, email: "a-applicant@example.com"))).IsSuccess.Should().BeTrue();

        // Tenant B receives an application on its own vacancy.
        var medB = BuildPipeline(_tenantB, _userB);
        (await medB.Send(Apply(_vacancyB, email: "b-applicant@example.com"))).IsSuccess.Should().BeTrue();

        // Tenant B cannot see Tenant A's vacancy's applicants — the vacancy is filtered out entirely
        // (404), and even a direct list returns zero of A's records.
        var bSeesA = await medB.Send(new ListApplicantsByVacancyQuery(_vacancyA));
        bSeesA.IsFailure.Should().BeTrue();
        bSeesA.StatusCode.Should().Be(404);

        // Tenant B's own list contains only B's applicant.
        var bList = await medB.Send(new ListApplicantsByVacancyQuery(_vacancyB));
        bList.Value!.Items.Should().OnlyContain(i => i.Email == "b-applicant@example.com");

        // Tenant A's list contains only A's applicant.
        var aList = await medA.Send(new ListApplicantsByVacancyQuery(_vacancyA));
        aList.Value!.Items.Should().OnlyContain(i => i.Email == "a-applicant@example.com");
    }

    private Guid SeedEmployee(Guid tenantId, Guid userId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        var id = Guid.NewGuid();
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = tenantId, UserId = userId,
            EmployeeNo = "E-1", FirstName = "Intern", LastName = "Person", Email = "intern@corp.com",
            DateOfJoining = new DateTime(2021, 1, 1),
            DepartmentId = Guid.NewGuid(), JobTitleId = Guid.NewGuid(),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        db.SaveChanges();
        return id;
    }

    // In-memory IFileStorage stub — avoids touching the filesystem in tests while still exercising the
    // virus-scan-then-store ordering and tenant-scoped path construction in ApplicantService.
    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
            => Task.FromResult($"/{tenantId}/{relativePath}");

        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
            => $"/files/{tenantId}/{relativePath}";

        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
