using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Observability;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog.Context;

namespace HRM.Infrastructure.Services;

/// <summary>
/// P3/RLS increment 2c — the job-side counterpart to <c>TenantTransactionBehavior</c>. See
/// <see cref="ITenantJobRunner"/> for the full rationale.
///
/// <para>Resolved from the per-tenant DI scope the calling job already creates, so the
/// <see cref="ITenantContext"/> and <see cref="AppDbContext"/> it uses are the SAME scoped instances the job's
/// per-tenant services use — that is what makes <c>SetTenant</c> here visible to the work, and the runner's
/// transaction the one the work's <c>SaveChanges</c> enlists in.</para>
/// </summary>
public sealed class TenantJobRunner : ITenantJobRunner
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly bool _rlsEnabled;

    public TenantJobRunner(AppDbContext db, ITenantContext tenant, IConfiguration configuration)
    {
        _db = db;
        _tenant = tenant;
        _rlsEnabled = configuration.GetValue("Rls:Enabled", false);
    }

    public async Task RunForTenantAsync(
        Guid tenantId,
        string subdomain,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        // Always establish the tenant context first. This also publishes the AmbientTenant (AsyncLocal) so the
        // connection router picks hrm_app + the EF global query filter and cache prefix scope to this tenant —
        // the behaviour every per-tenant job already relies on today.
        _tenant.SetTenant(tenantId, subdomain, TenantStatus.Active);

        // GAP-024 — attribute this iteration's log lines to THIS tenant.
        //
        // JobLogContextFilter can only read a tenant off a job's own top-level `tenantId` ARGUMENT. The ~19
        // sweep jobs (AutoClockOutJob, LeaveAccrualJob, ScheduledReportJob, WorkflowSlaEscalationJob,
        // ProcessCarryForwardExpiryJob, …) declare no such argument — they enumerate tenants internally and
        // call this runner once per tenant — so the filter pushed nothing and every interior line they emitted
        // was unattributable. A sweep job is not context-free; it is SEQUENTIALLY single-tenant, and this is
        // the seam where the current tenant is known. It is also the higher-risk class: one execution touches
        // every tenant's data, which is exactly when forensics needs to know which tenant a line came from.
        //
        // Scoping, not just tagging: LogContext is an AsyncLocal, so the property flows DOWN into `work` and
        // into every DI scope, task and EF query it creates, and stops at this method's boundary — so the
        // NEXT tenant's lines carry the next tenant and lines between iterations carry none.
        //
        // The `using` is defence-in-depth rather than the mechanism: because this is an `async` method,
        // AsyncTaskMethodBuilder.Start restores the caller's ExecutionContext, so the write cannot escape
        // even unpopped (verified — removing the `using` leaves the release arms green). Keep the push HERE,
        // inside the async seam. Moving it to a SYNCHRONOUS seam (e.g. into TenantContext.SetTenant) would
        // lose that containment and stain every later line on the thread with a stale tenant.
        //
        // Guid.Empty is deliberately NOT pushed, matching JobLogProperties.TenantIdOf: an all-zero tenant id
        // reads like a real scope to whoever is grepping the log during an incident, which is worse than the
        // field simply being absent.
        using IDisposable? tenantLogScope = tenantId == Guid.Empty
            ? null
            : LogContext.PushProperty(LogPropertyNames.TenantId, tenantId);

        // Non-breaking today: with Rls:Enabled false (or on the InMemory provider) we run the work directly with
        // NO transaction and NO raw SQL — identical to the bare SetTenant + body this replaces.
        if (!_rlsEnabled || !_db.Database.IsRelational())
        {
            await work(cancellationToken);
            return;
        }

        // Under RLS: set the GUC inside a retry-safe transaction (BUG-068/BUG-252 pattern — a user-initiated
        // BeginTransactionAsync throws under Npgsql's retrying execution strategy unless wrapped). The GUC is set
        // with is_local => true so it is transaction-scoped and cannot leak across pooled connections.
        var strategy = _db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            // set_config(..., is_local => true) == SET LOCAL but accepts a bind parameter, so the tenant id is
            // passed safely as a parameter (not string-interpolated into SQL).
            // nosemgrep: hrm-raw-sql-no-tenant-predicate -- this statement IS the tenant mechanism: it sets the app.current_tenant GUC that every RLS policy reads. It cannot itself be tenant-predicated.
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant', {tenantId.ToString()}, true)",
                cancellationToken);

            await work(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });
    }
}
