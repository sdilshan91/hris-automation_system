// ============================================================================
// US-PAY-008: Payroll approval-workflow — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR pipeline +
// ITenantContext-driven global query filters), covering:
//   - AC-1: submit creates a Submitted history row and moves the run to AwaitingApproval.
//   - AC-2: approve (by a different approver) -> Approved + an Approved history row.
//   - AC-3: reject with a reason -> Rejected + the reason stored in the audit trail.
//   - AC-4: a multi-step submit routes sequentially (stays AwaitingApproval after step 1, Approved after 2).
//   - AC-5/BR-1: finalize an Approved run -> Finalized; a direct ReviewPending finalize is blocked.
//   - FR-4: the approval summary exposes totals + previous-month net + variance.
//   - BR-8: tenant isolation — Tenant B cannot see/act on Tenant A's run.
//
// PROVIDER: InMemory through the real composed pipeline — same rationale as the other Payroll/Attendance
// integration tests (the verify gate runs `dotnet test` with no PostgreSQL / Docker).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.Commands;
using HRM.Application.Features.Payroll.Queries;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration;

public sealed class PayrollApprovalIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _hrUser = Guid.NewGuid();
    private readonly Guid _financeUser = Guid.NewGuid();

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
        public string Email => "u@t.com";
        public Guid TenantId { get; init; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
    }

    private IServiceProvider Provider(Guid tenantId, Guid actingUser)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser { TenantId = tenantId, UserId = actingUser });
        services.AddSingleton<IPayrollNotificationService, LogOnlyPayrollNotificationService>();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IPayrollApprovalService, PayrollApprovalService>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SubmitPayrollForApprovalCommand).Assembly));
        return services.BuildServiceProvider();
    }

    private IMediator Pipeline(Guid tenantId, Guid actingUser) => Provider(tenantId, actingUser).GetRequiredService<IMediator>();

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private async Task<Guid> SeedRunAsync(Guid tenantId, PayrollRunStatus status, int payMonth = 5, int payYear = 2026,
        Guid? submittedBy = null, int? step = null, int? totalSteps = null, Guid? instanceId = null,
        decimal totalNet = 80_000m)
    {
        using var db = Db(tenantId);
        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = tenantId, PayMonth = payMonth, PayYear = payYear,
            Status = status, InitiatedBy = _hrUser, InitiatedAt = DateTime.UtcNow,
            TotalEmployees = 2, ProcessedEmployees = 2, TotalGross = totalNet, TotalNet = totalNet,
            SubmittedBy = submittedBy, CurrentApprovalStep = step, TotalApprovalSteps = totalSteps,
            CurrentWorkflowInstanceId = instanceId, FinalizedAt = status == PayrollRunStatus.Finalized ? DateTime.UtcNow : null,
        });
        await db.SaveChangesAsync();
        return runId;
    }

    /// <summary>Seeds two eligible approvers so the maker-checker rule is in force (no small-team exception).</summary>
    private async Task SeedTwoApproversAsync(Guid tenantId)
    {
        using var db = Db(tenantId);
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = tenantId, Name = "Approver" });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Payroll.Approve });
        foreach (var uid in new[] { _hrUser, _financeUser })
        {
            var utId = Guid.NewGuid();
            db.UserTenants.Add(new UserTenant { Id = utId, UserId = uid, TenantId = tenantId, Status = UserTenantStatus.Active });
            db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        }
        await db.SaveChangesAsync();
    }

    // ── AC-1: submit -> AwaitingApproval + history ──────────────────────────────

    [Fact]
    public async Task Submit_CreatesHistory_AndSetsAwaitingApproval()
    {
        var runId = await SeedRunAsync(_tenantA, PayrollRunStatus.ReviewPending);
        var mediator = Pipeline(_tenantA, _hrUser);

        var result = await mediator.Send(new SubmitPayrollForApprovalCommand(runId, null, "go", "9.9.9.9"));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(PayrollRunStatus.AwaitingApproval));

        using var db = Db(_tenantA);
        (await db.PayrollApprovalHistories.CountAsync(h => h.PayrollRunId == runId)).Should().Be(1);
    }

    // ── AC-2: approve -> Approved + history ─────────────────────────────────────

    [Fact]
    public async Task Approve_ByDifferentApprover_SetsApproved_AndHistory()
    {
        await SeedTwoApproversAsync(_tenantA);
        var runId = await SeedRunAsync(_tenantA, PayrollRunStatus.AwaitingApproval, submittedBy: _hrUser,
            step: 1, totalSteps: 1, instanceId: Guid.NewGuid());

        var result = await Pipeline(_tenantA, _financeUser).Send(new ApprovePayrollRunCommand(runId, null, null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(PayrollRunStatus.Approved));

        using var db = Db(_tenantA);
        (await db.PayrollApprovalHistories.CountAsync(h => h.PayrollRunId == runId && h.Action == PayrollApprovalAction.Approved))
            .Should().Be(1);
    }

    // ── AC-3: reject -> Rejected + reason in trail ──────────────────────────────

    [Fact]
    public async Task Reject_StoresReason_InAuditTrail()
    {
        await SeedTwoApproversAsync(_tenantA);
        var runId = await SeedRunAsync(_tenantA, PayrollRunStatus.AwaitingApproval, submittedBy: _hrUser,
            step: 1, totalSteps: 1, instanceId: Guid.NewGuid());

        var result = await Pipeline(_tenantA, _financeUser)
            .Send(new RejectPayrollRunCommand(runId, "Statutory deductions are missing for 3 staff.", null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(PayrollRunStatus.Rejected));

        using var db = Db(_tenantA);
        var history = await db.PayrollApprovalHistories.SingleAsync(h => h.PayrollRunId == runId && h.Action == PayrollApprovalAction.Rejected);
        history.Comments.Should().Be("Statutory deductions are missing for 3 staff.");
    }

    // ── AC-4: multi-step routes sequentially ────────────────────────────────────

    [Fact]
    public async Task MultiStep_RoutesSequentially()
    {
        var runId = await SeedRunAsync(_tenantA, PayrollRunStatus.AwaitingApproval, submittedBy: _hrUser,
            step: 1, totalSteps: 2, instanceId: Guid.NewGuid());

        var first = await Pipeline(_tenantA, _financeUser).Send(new ApprovePayrollRunCommand(runId, null, null));
        first.Value!.Status.Should().Be(nameof(PayrollRunStatus.AwaitingApproval));

        var second = await Pipeline(_tenantA, _financeUser).Send(new ApprovePayrollRunCommand(runId, null, null));
        second.Value!.Status.Should().Be(nameof(PayrollRunStatus.Approved));
    }

    // ── AC-5 / BR-1: finalize locks; direct finalize blocked ────────────────────

    [Fact]
    public async Task Finalize_FromApproved_LocksRun()
    {
        var runId = await SeedRunAsync(_tenantA, PayrollRunStatus.Approved);

        var result = await Pipeline(_tenantA, _hrUser).Send(new FinalizePayrollRunCommand(runId, null));

        result.IsSuccess.Should().BeTrue();
        result.Value!.Status.Should().Be(nameof(PayrollRunStatus.Finalized));

        using var db = Db(_tenantA);
        var run = await db.PayrollRuns.SingleAsync(r => r.Id == runId);
        run.FinalizedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Finalize_DirectFromReviewPending_IsBlocked()
    {
        var runId = await SeedRunAsync(_tenantA, PayrollRunStatus.ReviewPending);

        var result = await Pipeline(_tenantA, _hrUser).Send(new FinalizePayrollRunCommand(runId, null));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("approval_required");
    }

    // ── FR-4: summary with previous-month variance ──────────────────────────────

    [Fact]
    public async Task Summary_ComputesPreviousMonthVariance()
    {
        // Prior FINALIZED run (April) with net 100k; current (May) run with net 110k -> +10% variance.
        await SeedRunAsync(_tenantA, PayrollRunStatus.Finalized, payMonth: 4, payYear: 2026, totalNet: 100_000m);
        var runId = await SeedRunAsync(_tenantA, PayrollRunStatus.AwaitingApproval, payMonth: 5, payYear: 2026,
            submittedBy: _hrUser, step: 1, totalSteps: 1, instanceId: Guid.NewGuid(), totalNet: 110_000m);

        var result = await Pipeline(_tenantA, _financeUser).Send(new GetPayrollApprovalSummaryQuery(runId));

        result.IsSuccess.Should().BeTrue();
        result.Value!.PreviousMonthTotalNet.Should().Be(100_000m);
        result.Value.VariancePercentage.Should().Be(10m);
    }

    // ── BR-8: tenant isolation ──────────────────────────────────────────────────

    [Fact]
    public async Task TenantB_CannotSeeTenantARun()
    {
        var runId = await SeedRunAsync(_tenantA, PayrollRunStatus.ReviewPending);

        // Tenant B submits the same runId -> the global filter hides it -> 404.
        var result = await Pipeline(_tenantB, _hrUser).Send(new SubmitPayrollForApprovalCommand(runId, null, null, null));

        result.IsFailure.Should().BeTrue();
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("run_not_found");
    }
}
