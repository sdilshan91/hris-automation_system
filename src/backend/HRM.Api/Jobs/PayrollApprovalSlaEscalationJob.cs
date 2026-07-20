using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-PAY-008 FR-3 (ISSUE-173): recurring Hangfire job that escalates payroll-run approvals whose current-step SLA
/// has elapsed. Runs at system level (outside a request scope): it enumerates active tenants and calls the per-tenant
/// <see cref="IPayrollApprovalSlaEscalator"/> in a fresh DI scope each, via <see cref="ITenantJobRunner"/> (so the
/// tenant context — and, gated on Rls:Enabled, the app.current_tenant GUC — is set inside the RLS backstop).
/// The escalator's conditional compare-and-swap makes each breached run escalate at most once (idempotent).
/// Mirrors <see cref="WorkflowSlaEscalationJob"/>.
/// </summary>
public sealed class PayrollApprovalSlaEscalationJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PayrollApprovalSlaEscalationJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await db.Tenants.IgnoreQueryFilters()
                .Where(t => !t.IsDeleted
                    && t.Status != TenantStatus.Terminated
                    && t.Status != TenantStatus.Terminating)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
        }

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
            var escalator = scope.ServiceProvider.GetRequiredService<IPayrollApprovalSlaEscalator>();
            var n = 0;
            await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}",
                async ct => n = await escalator.EscalateDueStepsAsync(ct), cancellationToken);
            total += n;
        }

        Log.Information(
            "PayrollApprovalSlaEscalationJob escalated {Count} approval(s) across {Tenants} tenant(s).",
            total, tenantIds.Count);
    }
}
