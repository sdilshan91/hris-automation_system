// ============================================================================
// US-ONB-001: Onboarding checklist template integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (with the MediatR
// pipeline, the FluentValidation ValidationBehavior, and the global query filters), covering:
//   - Happy path: create persists template + tasks with the session tenant_id (AC-2/FR-5), mandatory
//     flag round-trips (AC-4), and GetById returns the nested tasks.
//   - Duplicate-name rejection in the same tenant (AC-3/BR-1).
//   - Tenant isolation: Tenant B cannot see Tenant A's templates (AC-5); the same name succeeds in B.
//   - Clone duplicates all tasks under a new id (FR-6).
//
// NOTE ON PROVIDER: mirrors the other recruitment/performance integration tests — EF Core InMemory
// through the real composed pipeline (no PostgreSQL, no Docker in the verify gate). The PG-specific
// schema (uuid[] columns, partial unique index) is validated by the `migrations` CI job applying the
// generated migration to real PG. The validation pipeline behavior is registered so BR-2/BR-3 short-
// circuit before the service, exactly as in production.
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

public sealed class OnboardingTemplateIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public OnboardingTemplateIntegrationTests()
    {
        SeedTenants();
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
        currentUser.Email.Returns("user@test.com");
        currentUser.IsAuthenticated.Returns(true);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton(currentUser);
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

        services.AddScoped<IOnboardingTemplateService, OnboardingTemplateService>();

        // Register validators + the validation pipeline behavior, exactly as in production.
        services.AddValidatorsFromAssembly(typeof(CreateChecklistTemplateCommand).Assembly);
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(CreateChecklistTemplateCommand).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private void SeedTenants()
    {
        var ctx = new MutableTenantContext { TenantId = _tenantA };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        using var db = new AppDbContext(options, ctx);

        db.Tenants.AddRange(
            new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme Corp" },
            new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });

        db.SaveChanges();
    }

    private static CreateChecklistTemplateCommand Create(string name = "Standard Onboarding") => new(
        name, "Reusable template", [], [], true,
        new[]
        {
            new CreateChecklistTemplateTask("Sign employment contract", "PDF", "Documentation",
                OnboardingResponsibleRole.HR, null, 0, true, 0),
            new CreateChecklistTemplateTask("Provision laptop", null, "IT Setup",
                OnboardingResponsibleRole.IT, null, 2, false, 1),
            new CreateChecklistTemplateTask("Welcome lunch", null, "Culture",
                OnboardingResponsibleRole.Manager, null, 3, false, 2),
        });

    // ── Happy path (AC-2 / AC-4 / FR-5) ─────────────────────────────────

    [Fact]
    public async Task Create_persists_template_with_tasks_and_session_tenant()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var result = await mediator.Send(Create());

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.TaskCount.Should().Be(3);
        dto.Tasks.Single(t => t.Title == "Sign employment contract").IsMandatory.Should().BeTrue();

        var read = await mediator.Send(new GetTemplateByIdQuery(dto.Id));
        read.IsSuccess.Should().BeTrue();
        read.Value!.Tasks.Should().HaveCount(3);
        read.Value!.IsUniversal.Should().BeTrue();
    }

    [Fact]
    public async Task Create_with_empty_tasks_is_blocked_by_validation_br2()
    {
        var mediator = BuildPipeline(_tenantA, _userA);

        var act = async () => await mediator.Send(new CreateChecklistTemplateCommand(
            "Empty One", null, [], [], true, []));

        await act.Should().ThrowAsync<ValidationException>();
    }

    // ── Duplicate name (AC-3 / BR-1) ────────────────────────────────────

    [Fact]
    public async Task Create_duplicate_name_in_same_tenant_is_rejected()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        (await mediator.Send(Create("Engineering Onboarding"))).IsSuccess.Should().BeTrue();

        var dup = await mediator.Send(Create("Engineering Onboarding"));

        dup.IsFailure.Should().BeTrue();
        dup.StatusCode.Should().Be(409);
        dup.ErrorCode.Should().Be("duplicate_name");
    }

    // ── Tenant isolation (AC-5) ─────────────────────────────────────────

    [Fact]
    public async Task Tenant_b_cannot_see_tenant_a_templates()
    {
        var mediatorA = BuildPipeline(_tenantA, _userA);
        var createdA = await mediatorA.Send(Create("Acme Onboarding"));
        createdA.IsSuccess.Should().BeTrue();

        var mediatorB = BuildPipeline(_tenantB, _userB);

        // B cannot read A's template by id.
        var byId = await mediatorB.Send(new GetTemplateByIdQuery(createdA.Value!.Id));
        byId.IsFailure.Should().BeTrue();
        byId.StatusCode.Should().Be(404);

        // B's list does not include A's template.
        var listB = await mediatorB.Send(new GetTemplatesQuery(null, null, 1, 50));
        listB.Value!.Items.Should().NotContain(t => t.Id == createdA.Value!.Id);
    }

    [Fact]
    public async Task Same_name_succeeds_in_a_different_tenant()
    {
        var mediatorA = BuildPipeline(_tenantA, _userA);
        (await mediatorA.Send(Create("Shared Name"))).IsSuccess.Should().BeTrue();

        var mediatorB = BuildPipeline(_tenantB, _userB);
        var resultB = await mediatorB.Send(Create("Shared Name"));

        resultB.IsSuccess.Should().BeTrue();
    }

    // ── Clone (FR-6) ────────────────────────────────────────────────────

    [Fact]
    public async Task Clone_duplicates_all_tasks_under_new_id()
    {
        var mediator = BuildPipeline(_tenantA, _userA);
        var created = await mediator.Send(Create("Cloneable Onboarding"));

        var clone = await mediator.Send(new CloneChecklistTemplateCommand(created.Value!.Id, null));

        clone.IsSuccess.Should().BeTrue();
        clone.Value!.Id.Should().NotBe(created.Value!.Id);
        clone.Value!.TemplateName.Should().Be("Cloneable Onboarding (Copy)");
        clone.Value!.Tasks.Should().HaveCount(3);
    }
}
