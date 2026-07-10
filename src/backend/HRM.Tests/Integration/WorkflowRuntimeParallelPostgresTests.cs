// ============================================================================
// US-ADM-011b (Part 2) AC-4 — parallel-group JOIN for WorkflowRuntimeService.DecideAsync.
//
// A single parallel step with TWO approvers (primary NamedUser + one additional approver child) fans out
// two Pending step-instances at the same order. The instance advances (here: completes) only after BOTH
// approve; the first approval yields StepRecorded and the request stays InProgress. Any single rejection
// short-circuits the whole group → the instance is Rejected.
//
// HARNESS = real Postgres via Testcontainers (NOT InMemory). The decide path runs inside the Npgsql
// retry-safe execution strategy + a FOR UPDATE row lock over the group, neither of which the InMemory
// provider supports (BUG-068 class). Schema via EnsureCreatedAsync() (mirrors the sibling *PostgresTests).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class WorkflowRuntimeParallelPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _approver1 = Guid.NewGuid();
    private readonly Guid _approver2 = Guid.NewGuid();
    private readonly Guid _definitionId = BaseEntity.NewUuidV7();
    private readonly MutableTenantContext _tc = new();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
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

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _tc.TenantId = _tenantId;

        await using var db = Db(User(_approver1));
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Id = _definitionId, TenantId = _tenantId, Name = "Parallel Leave Approval",
            EntityType = WorkflowEntityType.Leave, LineageId = _definitionId, Version = 1,
            Status = WorkflowStatus.Active, IsActive = true,
        });
        var stepId = BaseEntity.NewUuidV7();
        db.WorkflowSteps.Add(new WorkflowStep
        {
            Id = stepId, TenantId = _tenantId, WorkflowDefinitionId = _definitionId,
            StepOrder = 1, ApproverType = WorkflowApproverType.NamedUser, ApproverIdentifier = _approver1,
            IsParallel = true, SlaHours = 24,
        });
        // Additional parallel approver (approver2) — the group requires BOTH to approve (AC-4).
        db.WorkflowStepApprovers.Add(new WorkflowStepApprover
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, WorkflowStepId = stepId,
            ApproverType = WorkflowApproverType.NamedUser, ApproverIdentifier = _approver2,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static ICurrentUser User(Guid userId)
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(userId);
        u.IsAuthenticated.Returns(true);
        u.Email.Returns("approver@acme.com");
        return u;
    }

    private AppDbContext Db(ICurrentUser user) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(_tc), new AuditInterceptor(user))
            .Options, _tc);

    private WorkflowRuntimeService Runtime(AppDbContext db, ICurrentUser user) =>
        new(db, _tc, user, NullLogger<WorkflowRuntimeService>.Instance);

    private async Task<Guid> CreateInstanceAsync()
    {
        var entityId = Guid.NewGuid();
        await using var db = Db(User(_approver1));
        var created = await Runtime(db, User(_approver1))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, entityId, null,
                new Dictionary<string, object?> { ["days_requested"] = 3m });
        created.InstanceCreated.Should().BeTrue();
        return entityId;
    }

    // ── AC-4: both approvers must approve; the first approval is StepRecorded ───

    [Fact]
    public async Task ParallelStep_BothMustApprove_FirstIsStepRecorded_ThenApproved()
    {
        var entityId = await CreateInstanceAsync();

        // The group fanned out two Pending rows at order 1.
        await using (var verify = Db(User(_approver1)))
        {
            var group = await verify.WorkflowStepInstances.AsNoTracking()
                .Where(s => s.StepOrder == 1).ToListAsync();
            group.Should().HaveCount(2);
            group.Should().OnlyContain(s => s.Decision == WorkflowStepDecision.Pending);
        }

        // Approver 1 approves → the group is not yet complete → StepRecorded, request stays InProgress.
        await using (var db = Db(User(_approver1)))
        {
            var d1 = await Runtime(db, User(_approver1))
                .DecideAsync(WorkflowEntityType.Leave, entityId, WorkflowDecisionAction.Approve, "ok-1");
            d1.IsSuccess.Should().BeTrue(d1.Error);
            d1.Value!.Outcome.Should().Be(WorkflowDecisionOutcome.StepRecorded);
        }

        await using (var verify = Db(User(_approver1)))
        {
            var instance = await verify.WorkflowInstances.AsNoTracking().FirstAsync(i => i.EntityId == entityId);
            instance.Status.Should().Be(WorkflowInstanceStatus.InProgress);
        }

        // Approver 2 approves → the group completes → no further step → the instance is Approved.
        await using (var db = Db(User(_approver2)))
        {
            var d2 = await Runtime(db, User(_approver2))
                .DecideAsync(WorkflowEntityType.Leave, entityId, WorkflowDecisionAction.Approve, "ok-2");
            d2.IsSuccess.Should().BeTrue(d2.Error);
            d2.Value!.Outcome.Should().Be(WorkflowDecisionOutcome.InstanceApproved);
        }

        await using var final = Db(User(_approver1));
        var done = await final.WorkflowInstances.AsNoTracking().FirstAsync(i => i.EntityId == entityId);
        done.Status.Should().Be(WorkflowInstanceStatus.Approved);
        var rows = await final.WorkflowStepInstances.AsNoTracking()
            .Where(s => s.WorkflowInstanceId == done.Id && s.StepOrder == 1).ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().OnlyContain(s => s.Decision == WorkflowStepDecision.Approved);
    }

    // ── AC-4 / BR-2: any single rejection short-circuits the whole group ────────

    [Fact]
    public async Task ParallelStep_OneRejection_ShortCircuits_InstanceRejected()
    {
        var entityId = await CreateInstanceAsync();

        await using (var db = Db(User(_approver2)))
        {
            var d = await Runtime(db, User(_approver2))
                .DecideAsync(WorkflowEntityType.Leave, entityId, WorkflowDecisionAction.Reject, "no");
            d.IsSuccess.Should().BeTrue(d.Error);
            d.Value!.Outcome.Should().Be(WorkflowDecisionOutcome.InstanceRejected);
        }

        await using var verify = Db(User(_approver1));
        var instance = await verify.WorkflowInstances.AsNoTracking().FirstAsync(i => i.EntityId == entityId);
        instance.Status.Should().Be(WorkflowInstanceStatus.Rejected);
        instance.CompletedAt.Should().NotBeNull();
    }
}
