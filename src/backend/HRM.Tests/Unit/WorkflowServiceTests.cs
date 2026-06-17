// ============================================================================
// US-ADM-007: Approval-workflow DEFINITION management service tests.
// Covers create-with-steps (AC-2), versioning (AC-3/FR-3), plan limit (AC-4/FR-4),
// BR-2 one-active-per-type auto-archive, archive/restore (FR-6), FR-5 validation,
// and BR-7 cross-tenant isolation. EF Core InMemory only (no Testcontainers).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Workflows.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class WorkflowServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowServiceTests()
    {
        _tenantContext = MakeTenantContext(_tenantId);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());

        _logger = Substitute.For<ILogger<WorkflowService>>();
    }

    private static ITenantContext MakeTenantContext(Guid tenantId)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(tenantId);
        ctx.IsResolved.Returns(true);
        ctx.IsSystemContext.Returns(false);
        return ctx;
    }

    private AppDbContext Db(ITenantContext? ctx = null)
        => TestDbContextFactory.Create(ctx ?? _tenantContext, _dbName);

    private WorkflowService Service(ITenantContext? ctx = null)
        => new(Db(ctx), ctx ?? _tenantContext, _currentUser, _logger);

    private async Task SeedTenantAsync(int? maxWorkflows = null, Guid? tenantId = null)
    {
        // The Tenant query filter is only !IsDeleted, so any context can seed any tenant row.
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId ?? _tenantId,
            Subdomain = $"t-{(tenantId ?? _tenantId):N}".Substring(0, 12),
            Name = "Test Tenant",
            MaxWorkflows = maxWorkflows,
        });
        await db.SaveChangesAsync();
    }

    private static WorkflowStepRequest LineManagerStep(int order = 1, int sla = 24, string? condition = null)
        => new(order, "LineManager", null, false, sla, null, null, condition, false, null);

    private static CreateWorkflowRequest LeaveWorkflow(
        string name = "Standard Leave Approval",
        bool activate = true,
        IReadOnlyList<WorkflowStepRequest>? steps = null)
        => new(name, "Leave", activate, steps ?? new[] { LineManagerStep() });

    // ── AC-2: create a 3-step workflow with conditions, SLAs, escalation ─────

    [Fact]
    public async Task Create_ThreeStepLeaveWorkflow_StoresAllStepsConditionsAndEscalation()
    {
        await SeedTenantAsync();

        var steps = new[]
        {
            // Escalation to DepartmentHead (no identifier needed → no seeded-role dependency).
            new WorkflowStepRequest(1, "LineManager", null, false, 24, "DepartmentHead", null, null, false, null),
            new WorkflowStepRequest(2, "DepartmentHead", null, false, 48,
                null, null, "{\"field\":\"days_requested\",\"operator\":\">\",\"value\":5}", false, null),
            new WorkflowStepRequest(3, "LineManager", null, false, 24,
                null, null, "{\"field\":\"days_requested\",\"operator\":\">\",\"value\":15}", false, null),
        };

        var result = await Service().CreateAsync(LeaveWorkflow(steps: steps));

        result.IsSuccess.Should().BeTrue(result.Error);
        var dto = result.Value!;
        dto.Version.Should().Be(1);
        dto.Status.Should().Be("Active");
        dto.IsActive.Should().BeTrue();
        dto.Steps.Should().HaveCount(3);

        dto.Steps[0].SlaHours.Should().Be(24);
        dto.Steps[0].EscalationApproverType.Should().Be("DepartmentHead");
        dto.Steps[0].EscalationApproverIdentifier.Should().BeNull();
        dto.Steps[1].ConditionJson.Should().Contain("\"value\":5");
        dto.Steps[2].ConditionJson.Should().Contain("\"value\":15");

        // Persisted correctly.
        using var db = Db();
        (await db.WorkflowDefinitions.CountAsync()).Should().Be(1);
        (await db.WorkflowSteps.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Create_AsDraft_DoesNotActivate()
    {
        await SeedTenantAsync();

        var result = await Service().CreateAsync(LeaveWorkflow(activate: false));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Status.Should().Be("Draft");
        result.Value!.IsActive.Should().BeFalse();
    }

    // ── AC-3 / FR-3: editing an ACTIVE workflow creates a new version ────────

    [Fact]
    public async Task Update_ActiveWorkflow_CreatesNewVersionAndRetainsPrior()
    {
        await SeedTenantAsync();
        var created = (await Service().CreateAsync(LeaveWorkflow())).Value!;

        var updated = await Service().UpdateAsync(
            created.LineageId,
            new UpdateWorkflowRequest("Standard Leave Approval", new[] { LineManagerStep(sla: 48) }));

        updated.IsSuccess.Should().BeTrue(updated.Error);
        updated.Value!.Version.Should().Be(2);
        updated.Value!.IsActive.Should().BeTrue();
        updated.Value!.Steps[0].SlaHours.Should().Be(48);

        using var db = Db();
        var rows = await db.WorkflowDefinitions
            .Where(w => w.LineageId == created.LineageId)
            .OrderBy(w => w.Version)
            .ToListAsync();

        rows.Should().HaveCount(2); // prior version retained
        rows[0].Version.Should().Be(1);
        rows[0].IsActive.Should().BeFalse();
        rows[0].Status.Should().Be(WorkflowStatus.Archived);
        rows[1].Version.Should().Be(2);
        rows[1].IsActive.Should().BeTrue();
        rows[1].Status.Should().Be(WorkflowStatus.Active);
    }

    [Fact]
    public async Task Update_DraftWorkflow_EditsInPlaceWithoutNewVersion()
    {
        await SeedTenantAsync();
        var created = (await Service().CreateAsync(LeaveWorkflow(activate: false))).Value!;

        var updated = await Service().UpdateAsync(
            created.LineageId,
            new UpdateWorkflowRequest("Renamed Draft", new[] { LineManagerStep(sla: 12) }));

        updated.IsSuccess.Should().BeTrue(updated.Error);
        updated.Value!.Version.Should().Be(1); // still v1 (in-place edit)
        updated.Value!.Name.Should().Be("Renamed Draft");

        using var db = Db();
        (await db.WorkflowDefinitions.CountAsync(w => w.LineageId == created.LineageId)).Should().Be(1);
    }

    // ── AC-4 / FR-4: plan limit ─────────────────────────────────────────────

    [Fact]
    public async Task Create_WhenPlanLimitReached_RejectsWithExactMessage()
    {
        await SeedTenantAsync(maxWorkflows: 2);

        (await Service().CreateAsync(new CreateWorkflowRequest("W1", "Leave", true, new[] { LineManagerStep() })))
            .IsSuccess.Should().BeTrue();
        (await Service().CreateAsync(new CreateWorkflowRequest("W2", "Attendance", true, new[] { LineManagerStep() })))
            .IsSuccess.Should().BeTrue();

        var third = await Service().CreateAsync(
            new CreateWorkflowRequest("W3", "Expense", true, new[] { LineManagerStep() }));

        third.IsFailure.Should().BeTrue();
        third.Error.Should().Be(
            "You have reached the maximum number of workflows (2) for your plan. Please upgrade or archive an existing workflow.");
    }

    [Fact]
    public async Task Create_NullPlanLimit_AllowsUnlimited()
    {
        await SeedTenantAsync(maxWorkflows: null);

        for (var i = 0; i < 5; i++)
        {
            var r = await Service().CreateAsync(
                new CreateWorkflowRequest($"W{i}", "Leave", true, new[] { LineManagerStep() }));
            r.IsSuccess.Should().BeTrue(r.Error);
        }
    }

    // ── BR-2: one active workflow per entity type, auto-archive prior ────────

    [Fact]
    public async Task Create_SecondActiveForSameEntityType_AutoArchivesFirst()
    {
        await SeedTenantAsync();
        var first = (await Service().CreateAsync(LeaveWorkflow("First Leave"))).Value!;

        var second = await Service().CreateAsync(LeaveWorkflow("Second Leave"));
        second.IsSuccess.Should().BeTrue(second.Error);

        using var db = Db();
        var firstRow = await db.WorkflowDefinitions.FirstAsync(w => w.Id == first.Id);
        firstRow.IsActive.Should().BeFalse();
        firstRow.Status.Should().Be(WorkflowStatus.Archived);

        var actives = await db.WorkflowDefinitions
            .CountAsync(w => w.EntityType == WorkflowEntityType.Leave && w.IsActive);
        actives.Should().Be(1);
    }

    // ── FR-6: archive / restore ─────────────────────────────────────────────

    [Fact]
    public async Task Archive_RemovesFromPlanLimitCount_ThenAnotherCanBeCreated()
    {
        await SeedTenantAsync(maxWorkflows: 1);
        var created = (await Service().CreateAsync(LeaveWorkflow())).Value!;

        // At the limit: a second active create is rejected.
        (await Service().CreateAsync(new CreateWorkflowRequest("W2", "Attendance", true, new[] { LineManagerStep() })))
            .IsFailure.Should().BeTrue();

        // Archive the first → it no longer counts.
        (await Service().ArchiveAsync(created.Id)).IsSuccess.Should().BeTrue();

        var afterArchive = await Service().CreateAsync(
            new CreateWorkflowRequest("W2", "Attendance", true, new[] { LineManagerStep() }));
        afterArchive.IsSuccess.Should().BeTrue(afterArchive.Error);
    }

    [Fact]
    public async Task Restore_ReactivatesArchivedWorkflow()
    {
        await SeedTenantAsync();
        var created = (await Service().CreateAsync(LeaveWorkflow())).Value!;
        (await Service().ArchiveAsync(created.Id)).IsSuccess.Should().BeTrue();

        var restored = await Service().RestoreAsync(created.Id);

        restored.IsSuccess.Should().BeTrue(restored.Error);
        restored.Value!.IsActive.Should().BeTrue();
        restored.Value!.Status.Should().Be("Active");
    }

    [Fact]
    public async Task Restore_AutoArchivesCurrentActiveForSameType()
    {
        await SeedTenantAsync();
        var first = (await Service().CreateAsync(LeaveWorkflow("First"))).Value!;
        (await Service().ArchiveAsync(first.Id)).IsSuccess.Should().BeTrue();
        var second = (await Service().CreateAsync(LeaveWorkflow("Second"))).Value!;

        // Restoring the first must archive the second (one active per type).
        (await Service().RestoreAsync(first.Id)).IsSuccess.Should().BeTrue();

        using var db = Db();
        (await db.WorkflowDefinitions.FirstAsync(w => w.Id == second.Id)).IsActive.Should().BeFalse();
        (await db.WorkflowDefinitions.FirstAsync(w => w.Id == first.Id)).IsActive.Should().BeTrue();
    }

    // ── BR-6: delete ────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WithNoInFlightInstances_RemovesWorkflowAndSteps()
    {
        await SeedTenantAsync();
        var created = (await Service().CreateAsync(LeaveWorkflow())).Value!;

        var deleted = await Service().DeleteAsync(created.Id);

        deleted.IsSuccess.Should().BeTrue(deleted.Error);
        using var db = Db();
        (await db.WorkflowDefinitions.CountAsync(w => w.Id == created.Id)).Should().Be(0);
        (await db.WorkflowSteps.CountAsync(s => s.WorkflowDefinitionId == created.Id)).Should().Be(0);
    }

    // ── FR-5: validation ────────────────────────────────────────────────────

    [Fact]
    public async Task Create_ZeroSteps_Rejected()
    {
        await SeedTenantAsync();

        var result = await Service().CreateAsync(
            new CreateWorkflowRequest("No Steps", "Leave", true, Array.Empty<WorkflowStepRequest>()));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("at least one step");
    }

    [Fact]
    public async Task Create_InvalidApproverRoleReference_Rejected()
    {
        await SeedTenantAsync();

        var step = new WorkflowStepRequest(1, "Role", Guid.NewGuid(), false, 24, null, null, null, false, null);
        var result = await Service().CreateAsync(
            new CreateWorkflowRequest("Bad Role", "Leave", true, new[] { step }));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("does not exist in this tenant");
    }

    [Fact]
    public async Task Create_ValidApproverRoleReference_Accepted()
    {
        await SeedTenantAsync();
        var roleId = Guid.NewGuid();
        using (var db = Db())
        {
            db.Roles.Add(new Role { Id = roleId, TenantId = _tenantId, Name = "Approvers" });
            await db.SaveChangesAsync();
        }

        var step = new WorkflowStepRequest(1, "Role", roleId, false, 24, null, null, null, false, null);
        var result = await Service().CreateAsync(
            new CreateWorkflowRequest("Good Role", "Leave", true, new[] { step }));

        result.IsSuccess.Should().BeTrue(result.Error);
    }

    [Fact]
    public async Task Create_NonPositiveSla_Rejected()
    {
        await SeedTenantAsync();

        var result = await Service().CreateAsync(
            new CreateWorkflowRequest("Bad SLA", "Leave", true, new[] { LineManagerStep(sla: 0) }));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("SLA hours must be a positive integer");
    }

    [Fact]
    public async Task Create_MalformedCondition_Rejected()
    {
        await SeedTenantAsync();

        var result = await Service().CreateAsync(
            new CreateWorkflowRequest("Bad Cond", "Leave", true,
                new[] { LineManagerStep(condition: "{ not valid json") }));

        result.IsFailure.Should().BeTrue();
    }

    // ── BR-7: cross-tenant isolation ────────────────────────────────────────

    [Fact]
    public async Task List_DoesNotReturnAnotherTenantsWorkflows()
    {
        await SeedTenantAsync();
        await Service().CreateAsync(LeaveWorkflow());

        // Tenant B context: separate tenant id, same in-memory store.
        var tenantBId = Guid.NewGuid();
        var ctxB = MakeTenantContext(tenantBId);

        var listB = await Service(ctxB).ListAsync();

        listB.IsSuccess.Should().BeTrue();
        listB.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_AnotherTenantsWorkflow_Returns404()
    {
        await SeedTenantAsync();
        var created = (await Service().CreateAsync(LeaveWorkflow())).Value!;

        var ctxB = MakeTenantContext(Guid.NewGuid());
        var getB = await Service(ctxB).GetAsync(created.Id);

        getB.IsFailure.Should().BeTrue();
        getB.StatusCode.Should().Be(404);
    }

    // ── AC-1: list ──────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsWorkflowsOrderedByEntityType_WithStepCountAndStatus()
    {
        await SeedTenantAsync();
        await Service().CreateAsync(new CreateWorkflowRequest("Leave WF", "Leave", true,
            new[] { LineManagerStep(1), LineManagerStep(2) }));
        await Service().CreateAsync(new CreateWorkflowRequest("Attendance WF", "Attendance", true,
            new[] { LineManagerStep(1) }));

        var list = await Service().ListAsync();

        list.IsSuccess.Should().BeTrue();
        list.Value.Should().HaveCount(2);
        // Ordered by entity type: Attendance (1) before Leave (0)? Enum order: Leave=0, Attendance=1.
        list.Value!.Select(w => w.EntityType).Should().Equal("Leave", "Attendance");
        list.Value!.Single(w => w.EntityType == "Leave").StepCount.Should().Be(2);
        list.Value!.Single(w => w.EntityType == "Attendance").StepCount.Should().Be(1);
    }
}
