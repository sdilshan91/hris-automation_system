using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

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
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT set_config('app.current_tenant', {tenantId.ToString()}, true)",
                cancellationToken);

            await work(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });
    }
}
