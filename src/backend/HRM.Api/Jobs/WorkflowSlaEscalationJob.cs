using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// US-ADM-011b (AC-5/FR-8/NFR-3): recurring Hangfire job that escalates approval steps whose SLA has elapsed.
/// Runs at system level (outside a request scope): it enumerates active tenants and calls the per-tenant
/// <see cref="IWorkflowSlaEscalator"/> in a fresh DI scope each, via <see cref="ITenantJobRunner"/> (so the
/// tenant context — and, gated on Rls:Enabled, the app.current_tenant GUC — is set inside the RLS backstop).
/// The escalator's conditional compare-and-swap makes each breached step escalate at most once (idempotent).
/// </summary>
public sealed class WorkflowSlaEscalationJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public WorkflowSlaEscalationJob(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

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
            var escalator = scope.ServiceProvider.GetRequiredService<IWorkflowSlaEscalator>();
            var n = 0;
            await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}",
                async ct => n = await escalator.EscalateDueStepsAsync(ct), cancellationToken);
            total += n;
        }

        Log.Information(
            "WorkflowSlaEscalationJob escalated {Count} step(s) across {Tenants} tenant(s).",
            total, tenantIds.Count);
    }
}
