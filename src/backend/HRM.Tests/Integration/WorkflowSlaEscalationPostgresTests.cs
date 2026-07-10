// ============================================================================
// US-ADM-011b (Part 3) AC-5/NFR-3 — SLA escalation for WorkflowSlaEscalator.EscalateDueStepsAsync.
//
// A Pending step-instance whose SlaDueAt is in the past is escalated at most ONCE (idempotent conditional
// compare-and-swap). With a configured escalation target the breached row is closed (Escalated) and a fresh
// Pending row is opened for the target; running the escalator a second time creates nothing new. Without a
// target the row stays Pending but is stamped EscalatedAt once (no re-processing, no replacement row).
//
// HARNESS = real Postgres via Testcontainers — the CAS uses ExecuteUpdateAsync (relational-only) and the
// behavior is RLS-eligible; InMemory cannot exercise it. Schema via EnsureCreatedAsync().
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

public sealed class WorkflowSlaEscalationPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _originalApprover = Guid.NewGuid();
    private readonly Guid _escalationUser = Guid.NewGuid();
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

        await using var db = Db();
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(_tc))
            .Options, _tc);

    private WorkflowSlaEscalator Escalator(AppDbContext db) =>
        new(db, _tc, NullLogger<WorkflowSlaEscalator>.Instance);

    /// <summary>Seeds an Active definition (one step) + a live instance + a breached Pending step-instance.</summary>
    private async Task<(Guid InstanceId, Guid StepInstanceId)> SeedBreachedStepAsync(
        WorkflowApproverType? escalationType, Guid? escalationIdentifier)
    {
        var definitionId = BaseEntity.NewUuidV7();
        var instanceId = BaseEntity.NewUuidV7();
        var stepInstanceId = BaseEntity.NewUuidV7();
        var entityId = Guid.NewGuid();
        var past = DateTime.UtcNow.AddHours(-1);

        await using var db = Db();
        db.WorkflowDefinitions.Add(new WorkflowDefinition
        {
            Id = definitionId, TenantId = _tenantId, Name = "Leave Approval",
            EntityType = WorkflowEntityType.Leave, LineageId = definitionId, Version = 1,
            Status = WorkflowStatus.Active, IsActive = true,
        });
        db.WorkflowSteps.Add(new WorkflowStep
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, WorkflowDefinitionId = definitionId,
            StepOrder = 1, ApproverType = WorkflowApproverType.NamedUser, ApproverIdentifier = _originalApprover,
            SlaHours = 24, EscalationApproverType = escalationType, EscalationApproverIdentifier = escalationIdentifier,
        });
        db.WorkflowInstances.Add(new WorkflowInstance
        {
            Id = instanceId, TenantId = _tenantId, WorkflowDefinitionId = definitionId, LineageId = definitionId,
            Version = 1, EntityType = WorkflowEntityType.Leave, EntityId = entityId,
            Status = WorkflowInstanceStatus.InProgress, CurrentStepOrder = 1, RouteStepOrders = "1",
        });
        db.WorkflowStepInstances.Add(new WorkflowStepInstance
        {
            Id = stepInstanceId, TenantId = _tenantId, WorkflowInstanceId = instanceId,
            StepOrder = 1, ApproverType = WorkflowApproverType.NamedUser, ApproverIdentifier = _originalApprover,
            AssignedApproverUserId = _originalApprover, Decision = WorkflowStepDecision.Pending,
            SlaDueAt = past, EscalatedAt = null,
        });
        await db.SaveChangesAsync();
        return (instanceId, stepInstanceId);
    }

    // ── AC-5 / NFR-3: with a target, escalate once → Escalated + fresh Pending; idempotent on re-run ──

    [Fact]
    public async Task WithTarget_EscalatesOnce_ClosesBreachedRow_OpensReplacement_Idempotent()
    {
        var (instanceId, breachedId) = await SeedBreachedStepAsync(
            WorkflowApproverType.NamedUser, _escalationUser);

        int first, second;
        await using (var db = Db())
            first = await Escalator(db).EscalateDueStepsAsync();
        await using (var db = Db())
            second = await Escalator(db).EscalateDueStepsAsync();

        first.Should().Be(1);
        second.Should().Be(0, "the replacement's SLA is in the future and the breached row is no longer Pending");

        await using var verify = Db();
        var rows = await verify.WorkflowStepInstances.AsNoTracking()
            .Where(s => s.WorkflowInstanceId == instanceId && s.StepOrder == 1).ToListAsync();

        rows.Should().HaveCount(2, "the breached row plus exactly one replacement (no double-escalation)");
        var breached = rows.Single(r => r.Id == breachedId);
        breached.Decision.Should().Be(WorkflowStepDecision.Escalated);
        breached.EscalatedAt.Should().NotBeNull();

        var replacement = rows.Single(r => r.Id != breachedId);
        replacement.Decision.Should().Be(WorkflowStepDecision.Pending);
        replacement.AssignedApproverUserId.Should().Be(_escalationUser);
        replacement.SlaDueAt.Should().BeAfter(DateTime.UtcNow);
    }

    // ── AC-5: no configured target → stay Pending, stamp EscalatedAt once, no replacement ──

    [Fact]
    public async Task WithoutTarget_StampsEscalatedAtOnce_RowStaysPending_NoReplacement()
    {
        var (instanceId, breachedId) = await SeedBreachedStepAsync(escalationType: null, escalationIdentifier: null);

        int first, second;
        await using (var db = Db())
            first = await Escalator(db).EscalateDueStepsAsync();
        await using (var db = Db())
            second = await Escalator(db).EscalateDueStepsAsync();

        first.Should().Be(1);
        second.Should().Be(0, "EscalatedAt is stamped so the row is not re-processed");

        await using var verify = Db();
        var rows = await verify.WorkflowStepInstances.AsNoTracking()
            .Where(s => s.WorkflowInstanceId == instanceId && s.StepOrder == 1).ToListAsync();

        rows.Should().ContainSingle("no replacement row is opened when there is no escalation target");
        var row = rows.Single();
        row.Id.Should().Be(breachedId);
        row.Decision.Should().Be(WorkflowStepDecision.Pending, "the original approver can still act");
        row.EscalatedAt.Should().NotBeNull();
    }
}
