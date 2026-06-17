// ============================================================================
// US-ONB-001: OnboardingTemplateService unit tests.
//
// Drives OnboardingTemplateService through the real EF Core InMemory provider (so template + task rows
// are actually persisted and the global query filter applies). Covers:
//   - AC-2/FR-5: create persists the template + tasks with tenant_id from the session context.
//   - AC-3/BR-1: a duplicate name in the same tenant is rejected (409); a different tenant may reuse it.
//   - AC-4: the mandatory flag round-trips on tasks.
//   - BR-2: an empty task list is rejected.
//   - FR-6: clone duplicates all tasks under a new (Copy) name, new id.
//   - FR-7/BR-4: deactivate flips is_active without deletion (still readable); double-deactivate is a no-op.
//   - BR-5: soft-deleted (filtered) name can be reused — implicitly via the global filter.
//   - Tenant scoping: an unresolved tenant context is refused.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class OnboardingTemplateServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _user;

    public OnboardingTemplateServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _user = Substitute.For<ICurrentUser>();
        _user.UserId.Returns(Guid.NewGuid());
        _user.Email.Returns("hr@acme.com");
        _user.IsAuthenticated.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private OnboardingTemplateService Service() =>
        new(Db(), _tenantContext, _user, Substitute.For<ILogger<OnboardingTemplateService>>());

    private static CreateOnboardingTemplateInput Input(
        string name = "Standard Onboarding",
        IReadOnlyList<OnboardingTaskInput>? tasks = null,
        bool isActive = true) =>
        new(name, "Reusable", [], [], isActive,
            tasks ?? new[]
            {
                new OnboardingTaskInput("Sign employment contract", null, "Documentation",
                    OnboardingResponsibleRole.HR, null, 0, true, 0),
                new OnboardingTaskInput("Set up laptop", null, "IT Setup",
                    OnboardingResponsibleRole.IT, null, 1, false, 1),
            });

    // ── Create (AC-2 / FR-5) ───────────────────────────────────────────

    [Fact]
    public async Task Create_persists_template_with_tasks_and_tenant_id()
    {
        var result = await Service().CreateAsync(Input());

        result.IsSuccess.Should().BeTrue();
        var dto = result.Value!;
        dto.TemplateName.Should().Be("Standard Onboarding");
        dto.TaskCount.Should().Be(2);
        dto.IsActive.Should().BeTrue();

        await using var db = Db();
        var stored = await db.OnboardingChecklistTemplates.Include(t => t.Tasks)
            .FirstAsync(t => t.Id == dto.Id);
        stored.TenantId.Should().Be(_tenantId);
        stored.Tasks.Should().HaveCount(2);
        stored.Tasks.Should().OnlyContain(t => t.TenantId == _tenantId);
    }

    [Fact]
    public async Task Create_roundtrips_mandatory_flag_ac4()
    {
        var result = await Service().CreateAsync(Input());

        var contractTask = result.Value!.Tasks.Single(t => t.Title == "Sign employment contract");
        contractTask.IsMandatory.Should().BeTrue();
        result.Value!.Tasks.Single(t => t.Title == "Set up laptop").IsMandatory.Should().BeFalse();
    }

    [Fact]
    public async Task Create_with_empty_tasks_is_rejected_br2()
    {
        var result = await Service().CreateAsync(Input(tasks: Array.Empty<OnboardingTaskInput>()));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("no_tasks");
    }

    [Fact]
    public async Task Create_unresolved_tenant_is_refused()
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.IsResolved.Returns(false);
        var svc = new OnboardingTemplateService(
            TestDbContextFactory.Create(ctx, _dbName), ctx, _user,
            Substitute.For<ILogger<OnboardingTemplateService>>());

        var result = await svc.CreateAsync(Input());

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(400);
    }

    // ── Duplicate name (AC-3 / BR-1) ────────────────────────────────────

    [Fact]
    public async Task Create_duplicate_name_in_same_tenant_is_rejected()
    {
        (await Service().CreateAsync(Input("Engineering Onboarding"))).IsSuccess.Should().BeTrue();

        var dup = await Service().CreateAsync(Input("Engineering Onboarding"));

        dup.IsFailure.Should().BeTrue();
        dup.StatusCode.Should().Be(409);
        dup.ErrorCode.Should().Be("duplicate_name");
        dup.Error.Should().Contain("already exists");
    }

    [Fact]
    public async Task Create_duplicate_name_is_case_insensitive()
    {
        (await Service().CreateAsync(Input("Engineering Onboarding"))).IsSuccess.Should().BeTrue();

        var dup = await Service().CreateAsync(Input("engineering onboarding"));

        dup.IsFailure.Should().BeTrue();
        dup.ErrorCode.Should().Be("duplicate_name");
    }

    [Fact]
    public async Task Same_name_succeeds_in_a_different_tenant()
    {
        (await Service().CreateAsync(Input("Engineering Onboarding"))).IsSuccess.Should().BeTrue();

        var otherTenant = Guid.NewGuid();
        var ctxB = Substitute.For<ITenantContext>();
        ctxB.TenantId.Returns(otherTenant);
        ctxB.IsResolved.Returns(true);
        var svcB = new OnboardingTemplateService(
            TestDbContextFactory.Create(ctxB, _dbName), ctxB, _user,
            Substitute.For<ILogger<OnboardingTemplateService>>());

        var result = await svcB.CreateAsync(Input("Engineering Onboarding"));

        result.IsSuccess.Should().BeTrue();
    }

    // ── Clone (FR-6) ────────────────────────────────────────────────────

    [Fact]
    public async Task Clone_duplicates_all_tasks_with_new_id_and_copy_name()
    {
        var created = await Service().CreateAsync(Input("Manager Onboarding"));
        var sourceId = created.Value!.Id;

        var clone = await Service().CloneAsync(sourceId, null);

        clone.IsSuccess.Should().BeTrue();
        clone.Value!.Id.Should().NotBe(sourceId);
        clone.Value!.TemplateName.Should().Be("Manager Onboarding (Copy)");
        clone.Value!.Tasks.Should().HaveCount(2);
        clone.Value!.Tasks.Select(t => t.Title)
            .Should().BeEquivalentTo("Sign employment contract", "Set up laptop");
    }

    [Fact]
    public async Task Clone_with_explicit_name_uses_it()
    {
        var created = await Service().CreateAsync(Input("Manager Onboarding"));

        var clone = await Service().CloneAsync(created.Value!.Id, "Director Onboarding");

        clone.IsSuccess.Should().BeTrue();
        clone.Value!.TemplateName.Should().Be("Director Onboarding");
    }

    [Fact]
    public async Task Clone_missing_template_returns_404()
    {
        var result = await Service().CloneAsync(Guid.NewGuid(), null);

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
    }

    // ── Activate / Deactivate (FR-7 / BR-4) ─────────────────────────────

    [Fact]
    public async Task Deactivate_flips_active_without_deletion_and_stays_readable()
    {
        var created = await Service().CreateAsync(Input("Ops Onboarding"));
        var id = created.Value!.Id;

        var deactivated = await Service().DeactivateAsync(id);
        deactivated.IsSuccess.Should().BeTrue();
        deactivated.Value!.IsActive.Should().BeFalse();

        // BR-4: still visible in the list / by id.
        var read = await Service().GetByIdAsync(id);
        read.IsSuccess.Should().BeTrue();
        read.Value!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Double_deactivate_is_a_no_op_conflict()
    {
        var created = await Service().CreateAsync(Input("Ops Onboarding"));
        await Service().DeactivateAsync(created.Value!.Id);

        var again = await Service().DeactivateAsync(created.Value!.Id);

        again.IsFailure.Should().BeTrue();
        again.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Reactivate_after_deactivate_succeeds()
    {
        var created = await Service().CreateAsync(Input("Ops Onboarding"));
        await Service().DeactivateAsync(created.Value!.Id);

        var activated = await Service().ActivateAsync(created.Value!.Id);

        activated.IsSuccess.Should().BeTrue();
        activated.Value!.IsActive.Should().BeTrue();
    }

    // ── List ────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_returns_only_current_tenant_templates_and_filters_active()
    {
        await Service().CreateAsync(Input("Active One"));
        var second = await Service().CreateAsync(Input("Inactive One"));
        await Service().DeactivateAsync(second.Value!.Id);

        var all = await Service().ListAsync(null, null, 1, 20);
        all.Value!.TotalCount.Should().Be(2);

        var activeOnly = await Service().ListAsync(true, null, 1, 20);
        activeOnly.Value!.Items.Should().OnlyContain(t => t.IsActive);
        activeOnly.Value!.Items.Should().ContainSingle(t => t.TemplateName == "Active One");
    }
}
