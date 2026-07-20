// ============================================================================
// US-NTF-006 Phase 4 — RealPayrollNotificationService unit tests (US-PAY-003 AC-3 / US-PAY-008 AC-1).
//
// The Real service resolves recipients from the DB (no recipient flows in from callers) and dispatches BOTH legs
// (in-app + email) to them. The load-bearing behaviour is the RECIPIENT SPLIT:
//   - run-ready / submitted / finalized → the approver pool (users whose roles grant Payroll.Approve).
//   - approved / rejected / returned    → the run's SubmittedBy (the maker); skipped gracefully when null.
// It NEVER throws into the run/approval flow. These tests genuinely observe the approver-vs-submitter split.
//
//   #5  NotifyRunReadyForReviewAsync → the 2 approvers ONLY (not the non-approver, not the submitter),
//       EventKey="payroll_run_ready", both legs.
//   #6a submitted → approvers, payroll_approval_submitted.
//   #6b approved  → the submitter ONLY (not the approvers), payroll_approval_approved.
//   #6c approved with SubmittedBy=null → nothing dispatched, no throw.
//   #7  never-throw when the dispatcher throws.
//
// PROVIDER: EF Core InMemory (the service resolves approvers via UserTenants⋈UserTenantRoles⋈RolePermissions with
// IgnoreQueryFilters) + a hand RecordingDispatcher. Mirrors PayrollApprovalServiceTests' seeding.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class RealPayrollNotificationServiceTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _approver1 = Guid.NewGuid();
    private readonly Guid _approver2 = Guid.NewGuid();
    private readonly Guid _nonApprover = Guid.NewGuid();
    private readonly Guid _submitter = Guid.NewGuid(); // a maker who is NOT in the approver pool
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;

    public RealPayrollNotificationServiceTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private RealPayrollNotificationService Service(INotificationDispatcher dispatcher) =>
        new(Db(), dispatcher, NullLogger<RealPayrollNotificationService>.Instance);

    /// <summary>Records dispatched requests per leg; optionally throws to prove the never-throw contract.</summary>
    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        private readonly bool _throw;
        public List<NotificationRequest> InApp { get; } = new();
        public List<NotificationRequest> Email { get; } = new();
        public RecordingDispatcher(bool throwOnDispatch = false) => _throw = throwOnDispatch;

        public Task SendInAppAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("in-app dispatch boom");
            InApp.Add(request);
            return Task.CompletedTask;
        }

        public Task SendEmailAsync(NotificationRequest request, CancellationToken cancellationToken = default)
        {
            if (_throw) throw new InvalidOperationException("email dispatch boom");
            Email.Add(request);
            return Task.CompletedTask;
        }
    }

    /// <summary>Seeds two users holding a Payroll.Approve role + one user without it (the approver pool + a decoy).</summary>
    private async Task SeedApproverPoolAsync()
    {
        using var db = Db();
        var roleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = roleId, TenantId = _tenantId, Name = "Payroll Approver" });
        db.RolePermissions.Add(new RolePermission { RoleId = roleId, Permission = PermissionCatalog.Payroll.Approve });

        foreach (var uid in new[] { _approver1, _approver2 })
        {
            var utId = Guid.NewGuid();
            db.UserTenants.Add(new UserTenant { Id = utId, UserId = uid, TenantId = _tenantId, Status = UserTenantStatus.Active });
            db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = roleId });
        }

        // A tenant user WITHOUT the approver role (a role with no Payroll.Approve permission).
        var plainRoleId = Guid.NewGuid();
        db.Roles.Add(new Role { Id = plainRoleId, TenantId = _tenantId, Name = "Viewer" });
        var nonApproverUt = Guid.NewGuid();
        db.UserTenants.Add(new UserTenant { Id = nonApproverUt, UserId = _nonApprover, TenantId = _tenantId, Status = UserTenantStatus.Active });
        db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = nonApproverUt, RoleId = plainRoleId });

        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedRunAsync(Guid? submittedBy)
    {
        using var db = Db();
        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = _tenantId, PayMonth = 5, PayYear = 2026,
            Status = PayrollRunStatus.ReviewPending, InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
            TotalEmployees = 43, ProcessedEmployees = 42, SkippedEmployees = 1,
            SubmittedBy = submittedBy,
        });
        await db.SaveChangesAsync();
        return runId;
    }

    // ── #5: run-ready → the approver pool ONLY (recipient split observed) ──

    [Fact]
    public async Task NotifyRunReadyForReviewAsync_DispatchesToApproversOnly_BothLegs()
    {
        await SeedApproverPoolAsync();
        var runId = await SeedRunAsync(submittedBy: _submitter);
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyRunReadyForReviewAsync(_tenantId, runId, processed: 42, skipped: 1);

        // Both legs go to exactly the two approvers — NOT the non-approver and NOT the submitter.
        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _approver1, _approver2 });
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _approver1, _approver2 });
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_nonApprover);
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_submitter);

        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "payroll_run_ready");
        dispatcher.InApp.Should().OnlyContain(r => r.EventKey == "payroll_run_ready");
        dispatcher.Email.Should().OnlyContain(r => r.TenantId == _tenantId);
    }

    // ── #6a: submitted → approvers ──

    [Fact]
    public async Task NotifyApprovalEventAsync_Submitted_DispatchesToApprovers()
    {
        await SeedApproverPoolAsync();
        var runId = await SeedRunAsync(submittedBy: _submitter);
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyApprovalEventAsync(_tenantId, runId, "payroll-approval-submitted");

        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _approver1, _approver2 });
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "payroll_approval_submitted");
    }

    // ── #6b: approved → the SUBMITTER only (the other half of the split) ──

    [Fact]
    public async Task NotifyApprovalEventAsync_Approved_DispatchesToSubmitterOnly()
    {
        await SeedApproverPoolAsync();
        var runId = await SeedRunAsync(submittedBy: _submitter);
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyApprovalEventAsync(_tenantId, runId, "payroll-approval-approved");

        // Recipient split: approved goes to the maker (SubmittedBy), NOT the approver pool.
        dispatcher.Email.Should().ContainSingle();
        dispatcher.Email.Single().RecipientUserId.Should().Be(_submitter);
        dispatcher.Email.Single().RecipientUserId.Should().NotBe(_approver1);
        dispatcher.InApp.Single().RecipientUserId.Should().Be(_submitter);
        dispatcher.Email.Single().EventKey.Should().Be("payroll_approval_approved");
    }

    // ── #6c: approved with no SubmittedBy → nothing dispatched, no throw ──

    [Fact]
    public async Task NotifyApprovalEventAsync_Approved_NullSubmittedBy_SkipsGracefully()
    {
        await SeedApproverPoolAsync();
        var runId = await SeedRunAsync(submittedBy: null);
        var dispatcher = new RecordingDispatcher();

        var act = () => Service(dispatcher).NotifyApprovalEventAsync(_tenantId, runId, "payroll-approval-approved");

        await act.Should().NotThrowAsync();
        dispatcher.Email.Should().BeEmpty();
        dispatcher.InApp.Should().BeEmpty();
    }

    // ── #7: never-throw when the dispatcher throws ──

    [Fact]
    public async Task NotifyRunReadyForReviewAsync_DispatcherThrows_DoesNotThrow()
    {
        await SeedApproverPoolAsync();
        var runId = await SeedRunAsync(submittedBy: _submitter);
        var dispatcher = new RecordingDispatcher(throwOnDispatch: true);

        var act = () => Service(dispatcher).NotifyRunReadyForReviewAsync(_tenantId, runId, 42, 1);

        await act.Should().NotThrowAsync();
    }

    // ── ISSUE-173 FR-3: an SLA-escalated run notifies the current step's BACKUP ROLE holders. ──

    /// <summary>Seeds an AwaitingApproval run at step 1 (SubmittedBy the maker). Optionally a step-1 config with a backup role.</summary>
    private async Task<Guid> SeedAwaitingRunAtStep1Async(Guid? backupRoleId)
    {
        using var db = Db();
        var runId = BaseEntity.NewUuidV7();
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = _tenantId, PayMonth = 5, PayYear = 2026,
            Status = PayrollRunStatus.AwaitingApproval, InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
            TotalEmployees = 2, ProcessedEmployees = 2, SubmittedBy = _submitter,
            CurrentApprovalStep = 1, TotalApprovalSteps = 1,
        });
        if (backupRoleId is { } br)
            db.PayrollApprovalStepConfigs.Add(new PayrollApprovalStepConfig
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, StepNumber = 1, RoleId = br, BackupRoleId = br,
            });
        await db.SaveChangesAsync();
        return runId;
    }

    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task NotifyApprovalEventAsync_Escalated_DispatchesToBackupRoleHolders_NotTheGeneralPool()
    {
        await SeedApproverPoolAsync();        // the general pool (approver1/2) must NOT receive when a backup is configured.
        var backupUser = Guid.NewGuid();
        Guid backupRole;
        using (var db = Db())
        {
            backupRole = Guid.NewGuid();
            db.Roles.Add(new Role { Id = backupRole, TenantId = _tenantId, Name = "Backup Approver" });
            db.RolePermissions.Add(new RolePermission { RoleId = backupRole, Permission = PermissionCatalog.Payroll.Approve });
            var utId = Guid.NewGuid();
            db.UserTenants.Add(new UserTenant { Id = utId, UserId = backupUser, TenantId = _tenantId, Status = UserTenantStatus.Active });
            db.UserTenantRoles.Add(new UserTenantRole { UserTenantId = utId, RoleId = backupRole });
            await db.SaveChangesAsync();
        }
        var runId = await SeedAwaitingRunAtStep1Async(backupRole);
        var dispatcher = new RecordingDispatcher();

        await Service(dispatcher).NotifyApprovalEventAsync(_tenantId, runId, "payroll-approval-escalated");

        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { backupUser });
        dispatcher.InApp.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { backupUser });
        dispatcher.Email.Select(r => r.RecipientUserId).Should().NotContain(_approver1); // general pool NOT used when a backup is set
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "payroll_approval_escalated");
    }

    [Fact]
    [Trait("TC", "TC-PAY-008-07")]
    public async Task NotifyApprovalEventAsync_Escalated_NoBackupConfigured_FallsBackToApproverPool()
    {
        await SeedApproverPoolAsync();
        var runId = await SeedAwaitingRunAtStep1Async(backupRoleId: null); // no step config / no BackupRoleId

        var dispatcher = new RecordingDispatcher();
        await Service(dispatcher).NotifyApprovalEventAsync(_tenantId, runId, "payroll-approval-escalated");

        // Fallback: the Payroll.Approve approver pool (people who can actually clear the overdue run).
        dispatcher.Email.Select(r => r.RecipientUserId).Should().BeEquivalentTo(new Guid?[] { _approver1, _approver2 });
        dispatcher.Email.Should().OnlyContain(r => r.EventKey == "payroll_approval_escalated");
    }
}
