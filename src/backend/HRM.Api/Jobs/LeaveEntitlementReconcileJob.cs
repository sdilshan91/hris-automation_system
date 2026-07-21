using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace HRM.Api.Jobs;

/// <summary>
/// DF-4 / BUG-118: durability backstop for the best-effort post-commit recalc enqueue in
/// <c>LeaveEntitlementService.UpdateRuleAsync</c>. That method commits a rule edit and then does a
/// fire-and-forget <c>ILeaveEntitlementRecalcJobScheduler.Enqueue</c> — if Hangfire storage is unavailable in
/// that window the enqueue is lost and the leave type's already-accrued employees never converge to the new
/// rule. This recurring sweep re-runs the recalc for the CURRENT leave year across every active tenant so a
/// dropped enqueue self-heals within one cadence.
///
/// <para><b>Why this is safe to run daily.</b> <see cref="ILeaveEntitlementService.RecalculateEntitlementsAsync"/>
/// is fully idempotent: it writes an <c>Adjusted</c> delta only when the rule-derived target differs from what
/// was already granted, and writes NOTHING when the delta is zero. So a re-run over already-converged tenants is
/// a no-op — it never double-counts, never disturbs manual adjustments, overrides, or not-yet-accrued
/// employees. The fast-path enqueue in <c>UpdateRuleAsync</c> is unchanged; this only backstops it. The
/// convergence bound for a lost enqueue is the sweep cadence.</para>
///
/// <para>Mirrors <see cref="LeaveAccrualJob"/> (cross-tenant iteration + per-tenant
/// <see cref="ITenantJobRunner.RunForTenantAsync"/> scope) and <see cref="RecalcLeaveEntitlementsJob"/>
/// (resolves the current leave year via <see cref="ITenantLeaveYearResolver"/> and delegates to the same
/// recalc). Unlike <c>OnboardingOverdueSweepJob</c>, the recalc is drained UNCONDITIONALLY every run.</para>
/// </summary>
public sealed class LeaveEntitlementReconcileJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// The sweep credits/adjusts into the leave year TODAY falls in for each tenant, so "today" must be
    /// injectable (fiscal-year-aware label is date-dependent). Trailing-optional — mirrors
    /// <see cref="LeaveAccrualJob"/> — so production and existing fixtures keep the real clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    public LeaveEntitlementReconcileJob(IServiceScopeFactory scopeFactory, TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Log.Information("Starting LeaveEntitlementReconcileJob");

        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        // Same source/predicate as LeaveAccrualJob: only tenants that can have active leave data.
        List<Guid> tenantIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            tenantIds = await dbContext.Tenants
                .Where(t => !t.IsDeleted && (t.Status == TenantStatus.Active || t.Status == TenantStatus.Trial))
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
        }

        Log.Information("LeaveEntitlementReconcileJob: Processing {TenantCount} tenants", tenantIds.Count);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                // Fresh scope per tenant → fresh scoped DbContext + services (no cross-tenant state bleed).
                using var scope = _scopeFactory.CreateScope();

                var runner = scope.ServiceProvider.GetRequiredService<ITenantJobRunner>();
                var leaveYearResolver = scope.ServiceProvider.GetRequiredService<ITenantLeaveYearResolver>();
                var entitlementService = scope.ServiceProvider.GetRequiredService<ILeaveEntitlementService>();

                // RLS increment 2c: run the per-tenant body via the shared runner so it sets the tenant context
                // (and, gated on Rls:Enabled, the app.current_tenant GUC) — keeping it inside the RLS backstop.
                await runner.RunForTenantAsync(tenantId, $"tenant-{tenantId}", async ct =>
                {
                    // Match the recalc fast path exactly (UpdateRuleAsync / RecalcLeaveEntitlementsJob): the
                    // fiscal-year-aware current leave year from the shared resolver, then a null leaveTypeId
                    // to reconcile ALL leave types for the tenant. Drained UNCONDITIONALLY every run.
                    var leaveYear = await leaveYearResolver.LabelForAsync(today, ct);
                    await entitlementService.RecalculateEntitlementsAsync(leaveYear, leaveTypeId: null, ct);

                    Log.Information(
                        "LeaveEntitlementReconcileJob: Reconciled tenant {TenantId} for leave year {LeaveYear}",
                        tenantId, leaveYear);
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "LeaveEntitlementReconcileJob: Failed to reconcile tenant {TenantId}", tenantId);
                // Continue with other tenants; one tenant's failure must not abort the sweep for the rest.
            }
        }

        Log.Information("Completed LeaveEntitlementReconcileJob");
    }
}
