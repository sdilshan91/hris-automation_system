// ============================================================================
// US-ADM-011c (Part 2, Q6): the appended WorkflowEntityType.Overtime round-trips through the definition
// service — a workflow can be authored for the Overtime entity type and read back with that type.
// EF Core InMemory (no runtime state involved). Mirrors WorkflowServiceTests' harness.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Workflows.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class WorkflowOvertimeEnumTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;

    public WorkflowOvertimeEnumTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(Guid.NewGuid());
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private WorkflowService Service()
        => new(Db(), _tenantContext, _currentUser, Substitute.For<ILogger<WorkflowService>>());

    [Fact]
    public async Task Create_OvertimeWorkflow_RoundTripsThroughListAndGet()
    {
        using (var db = Db())
        {
            db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
            await db.SaveChangesAsync();
        }

        // A Draft (activate: false) Overtime workflow with one relative (LineManager) step — no plan-limit
        // check, no approver-reference lookups.
        var step = new WorkflowStepRequest(1, "LineManager", null, false, 24, null, null, null, false, null);
        var created = await Service().CreateAsync(
            new CreateWorkflowRequest("OT Pre-Approval", "Overtime", false, new[] { step }));

        created.IsSuccess.Should().BeTrue(created.Error);
        created.Value!.EntityType.Should().Be("Overtime");

        var list = await Service().ListAsync();
        list.IsSuccess.Should().BeTrue(list.Error);
        list.Value!.Should().Contain(w => w.EntityType == "Overtime");

        var got = await Service().GetAsync(created.Value!.Id);
        got.IsSuccess.Should().BeTrue(got.Error);
        got.Value!.EntityType.Should().Be("Overtime");
    }
}
