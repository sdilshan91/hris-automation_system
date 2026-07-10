// ============================================================================
// US-ADM-011b (Part 1): design-time MULTI-APPROVER (parallel) round-trip.
// A parallel step carries additional NamedUser approver ids (parallelApproverIdentifiers);
// they must persist as WorkflowStepApprover child rows, round-trip through GET, be replaced
// on update, and cascade on delete. EF Core InMemory (persistence + DTO mapping only).
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

public sealed class WorkflowMultiApproverTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<WorkflowService> _logger;

    private readonly Guid _approverA = Guid.NewGuid();
    private readonly Guid _approverB = Guid.NewGuid();
    private readonly Guid _approverC = Guid.NewGuid();

    public WorkflowMultiApproverTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());

        _logger = Substitute.For<ILogger<WorkflowService>>();
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);
    private WorkflowService Service() => new(Db(), _tenantContext, _currentUser, _logger);

    private async Task SeedAsync()
    {
        using var db = Db();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        foreach (var uid in new[] { _approverA, _approverB, _approverC })
            db.UserTenants.Add(new UserTenant
            {
                Id = BaseEntity.NewUuidV7(), UserId = uid, TenantId = _tenantId, Status = UserTenantStatus.Active,
            });
        await db.SaveChangesAsync();
    }

    /// <summary>A parallel step: LineManager primary approver + the given additional NamedUser approvers.</summary>
    private static WorkflowStepRequest ParallelStep(params Guid[] additional) =>
        new(1, "LineManager", null, IsParallel: true, 24, null, null, null, false, null,
            ParallelApproverIdentifiers: additional);

    [Fact]
    public async Task Create_ParallelStepWithApprovers_PersistsChildren_RoundTrips()
    {
        await SeedAsync();

        var create = new CreateWorkflowRequest(
            "Parallel Leave Approval", "Leave", Activate: false,
            new[] { ParallelStep(_approverA, _approverB) });

        var created = await Service().CreateAsync(create);
        created.IsSuccess.Should().BeTrue(created.Error);

        // Child rows persisted.
        using (var db = Db())
            (await db.WorkflowStepApprovers.CountAsync()).Should().Be(2);

        // Round-trips through GET.
        var got = await Service().GetAsync(created.Value!.Id);
        got.IsSuccess.Should().BeTrue(got.Error);
        var step = got.Value!.Steps.Single();
        step.IsParallel.Should().BeTrue();
        step.ParallelApproverIdentifiers.Should().BeEquivalentTo(new[] { _approverA, _approverB });
    }

    [Fact]
    public async Task Update_Draft_ReplacesParallelApprovers()
    {
        await SeedAsync();

        var created = await Service().CreateAsync(new CreateWorkflowRequest(
            "Parallel Leave Approval", "Leave", Activate: false,
            new[] { ParallelStep(_approverA, _approverB) }));
        created.IsSuccess.Should().BeTrue(created.Error);

        // Replace the two approvers with a single different one.
        var update = new UpdateWorkflowRequest("Parallel Leave Approval", new[] { ParallelStep(_approverC) });
        var updated = await Service().UpdateAsync(created.Value!.LineageId, update);
        updated.IsSuccess.Should().BeTrue(updated.Error);

        updated.Value!.Steps.Single().ParallelApproverIdentifiers.Should().BeEquivalentTo(new[] { _approverC });

        // Old children removed, new one persisted (Draft edits in place — no retained version).
        using var db = Db();
        var rows = await db.WorkflowStepApprovers.Select(a => a.ApproverIdentifier).ToListAsync();
        rows.Should().ContainSingle().Which.Should().Be(_approverC);
    }

    [Fact]
    public async Task Delete_CascadesParallelApprovers()
    {
        await SeedAsync();

        var created = await Service().CreateAsync(new CreateWorkflowRequest(
            "Parallel Leave Approval", "Leave", Activate: false,
            new[] { ParallelStep(_approverA, _approverB) }));
        created.IsSuccess.Should().BeTrue(created.Error);

        var deleted = await Service().DeleteAsync(created.Value!.Id);
        deleted.IsSuccess.Should().BeTrue(deleted.Error);

        using var db = Db();
        (await db.WorkflowStepApprovers.CountAsync()).Should().Be(0);
        (await db.WorkflowSteps.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_ParallelApprover_NotTenantMember_IsRejected()
    {
        await SeedAsync();

        var stranger = Guid.NewGuid(); // never seeded as a tenant member
        var create = new CreateWorkflowRequest(
            "Parallel Leave Approval", "Leave", Activate: false,
            new[] { ParallelStep(_approverA, stranger) });

        var result = await Service().CreateAsync(create);
        result.IsFailure.Should().BeTrue();
        // NOTE: CreateAsync forwards only the validation message + status (it drops the "invalid_approver" code),
        // so assert on the message. The underlying ValidateStepsAsync rejects the non-member parallel approver.
        result.Error.Should().Contain("is not a member of this tenant");
    }
}
