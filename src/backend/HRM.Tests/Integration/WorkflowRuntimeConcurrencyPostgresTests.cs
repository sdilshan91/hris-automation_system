// ============================================================================
// US-ADM-011 (Phase 1) AC-12/NFR-4 — concurrency regression for WorkflowRuntimeService.DecideAsync.
//
// Two approvers act on the SAME active step-instance concurrently. The advance runs inside the Npgsql
// retry-safe execution strategy wrapping a transaction that takes a pessimistic `SELECT ... FOR UPDATE`
// row lock on the active step-instance row (mirrors AuthService.RunFailedAttemptAsync / the BUG-068 class).
// Exactly one decision must win; the instance must NOT be double-advanced (only ONE step-2 instance is
// materialized) and the loser must get 409 step_already_decided.
//
// HARNESS = real Postgres via Testcontainers (NOT InMemory). This is load-bearing: the InMemory provider
// has no transactions and no FOR UPDATE, so the single-winner guarantee can only be exercised on Postgres.
// Schema is built with EnsureCreatedAsync() (mirrors the other *ConcurrencyPostgresTests — MigrateAsync is
// avoided because an unrelated uncommitted model change would trip PendingModelChangesWarning).
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

public sealed class WorkflowRuntimeConcurrencyPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _approver1 = Guid.NewGuid();
    private readonly Guid _approver2 = Guid.NewGuid();
    private readonly Guid _definitionId = BaseEntity.NewUuidV7();
    private readonly Guid _entityId = Guid.NewGuid();
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
            Id = _definitionId, TenantId = _tenantId, Name = "Leave Approval",
            EntityType = WorkflowEntityType.Leave, LineageId = _definitionId, Version = 1,
            Status = WorkflowStatus.Active, IsActive = true,
        });
        // Two sequential NamedUser steps so a winning step-1 approval ADVANCES to step 2 — this is what a
        // double-advance would corrupt (it would create two step-2 rows).
        db.WorkflowSteps.AddRange(
            new WorkflowStep
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, WorkflowDefinitionId = _definitionId,
                StepOrder = 1, ApproverType = WorkflowApproverType.NamedUser, ApproverIdentifier = _approver1, SlaHours = 24,
            },
            new WorkflowStep
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, WorkflowDefinitionId = _definitionId,
                StepOrder = 2, ApproverType = WorkflowApproverType.NamedUser, ApproverIdentifier = _approver2, SlaHours = 24,
            });
        await db.SaveChangesAsync();

        // Instantiate the workflow (creates the instance + the active step-1 instance).
        var created = await Runtime(db, User(_approver1))
            .CreateInstanceOnSubmitAsync(WorkflowEntityType.Leave, _entityId, null,
                new Dictionary<string, object?> { ["days_requested"] = 3m });
        created.InstanceCreated.Should().BeTrue();
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

    // ── AC-12: two concurrent approvals of the same step → exactly one wins ────

    [Fact]
    public async Task ConcurrentApprovals_SameStep_ExactlyOneWins_NoDoubleAdvance_AC12()
    {
        // Both callers are the step-1 approver and approve step 1 at the same time (each on its own context).
        async Task<Result> DecideOnceAsync()
        {
            await using var db = Db(User(_approver1));
            var decision = await Runtime(db, User(_approver1))
                .DecideAsync(WorkflowEntityType.Leave, _entityId, WorkflowDecisionAction.Approve, "ok");
            return decision.IsSuccess
                ? Result.Winner(decision.Value!.Outcome)
                : Result.Loser(decision.StatusCode, decision.ErrorCode);
        }

        var t1 = Task.Run(DecideOnceAsync);
        var t2 = Task.Run(DecideOnceAsync);
        var results = await Task.WhenAll(t1, t2);

        // Exactly one winner, one loser (409 step_already_decided).
        results.Count(r => r.IsWinner).Should().Be(1, "exactly one concurrent decision may advance the step");
        var loser = results.Single(r => !r.IsWinner);
        loser.StatusCode.Should().Be(409);
        loser.ErrorCode.Should().Be("step_already_decided");

        // The winning decision advanced step 1 → step 2 EXACTLY ONCE (no double-advance).
        await using var verify = Db(User(_approver1));
        var instance = await verify.WorkflowInstances.AsNoTracking().FirstAsync(i => i.EntityId == _entityId);
        instance.Status.Should().Be(WorkflowInstanceStatus.InProgress);
        instance.CurrentStepOrder.Should().Be(2);

        var step1 = await verify.WorkflowStepInstances.AsNoTracking()
            .Where(s => s.WorkflowInstanceId == instance.Id && s.StepOrder == 1).ToListAsync();
        step1.Should().ContainSingle().Which.Decision.Should().Be(WorkflowStepDecision.Approved);

        var step2 = await verify.WorkflowStepInstances.AsNoTracking()
            .Where(s => s.WorkflowInstanceId == instance.Id && s.StepOrder == 2).ToListAsync();
        step2.Should().ContainSingle("the step must be advanced exactly once — no duplicate step-2 instance");
        step2[0].Decision.Should().Be(WorkflowStepDecision.Pending);
    }

    /// <summary>Tiny value holder for the concurrent decision outcome (avoids a name clash with the app Result).</summary>
    private sealed class Result
    {
        public bool IsWinner { get; private init; }
        public int? StatusCode { get; private init; }
        public string? ErrorCode { get; private init; }

        public static Result Winner(WorkflowDecisionOutcome _) => new() { IsWinner = true };
        public static Result Loser(int? statusCode, string? errorCode) =>
            new() { IsWinner = false, StatusCode = statusCode, ErrorCode = errorCode };
    }
}
