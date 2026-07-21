using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Infrastructure.Services;

/// <summary>
/// DF-50 — see <see cref="IParallelTenantScopeRunner"/>. Fans out independent per-tenant work over bounded
/// concurrency, giving each item its OWN DI scope (fresh <c>AppDbContext</c> + collaborators) and re-seeding
/// that scope's <see cref="ITenantContext"/> to the captured parent tenant BEFORE the work runs.
///
/// <para>The re-seed is the whole point: a fresh child scope has an unresolved tenant context, and the EF
/// global query filter is fail-open, so skipping <see cref="ITenantContext.SetTenant"/> would leak every
/// tenant's rows. The parent tenant identity is snapshotted once (before fan-out) so all children see a stable,
/// consistent tenant even though each runs on a different thread.</para>
///
/// <para><b>Single-tenant fan-out only.</b> Every child here is seeded with the SAME parent tenant. This is safe
/// with the singleton EF second-level-cache key-prefix provider + the <c>AmbientTenant</c> AsyncLocal because all
/// concurrent branches carry one identical tenant. If a FUTURE caller ever fans out across DIFFERENT tenants in one
/// logical unit, re-audit that AsyncLocal / cache-prefix interaction under true multi-tenant parallelism before
/// relying on this runner.</para>
/// </summary>
public sealed class ParallelTenantScopeRunner : IParallelTenantScopeRunner
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantContext _tenant;

    public ParallelTenantScopeRunner(IServiceScopeFactory scopeFactory, ITenantContext tenant)
    {
        _scopeFactory = scopeFactory;
        _tenant = tenant;
    }

    public async Task<IReadOnlyList<T>> RunAllAsync<T>(
        IEnumerable<Func<IServiceProvider, CancellationToken, Task<T>>> work,
        int maxDegreeOfParallelism,
        CancellationToken ct = default)
    {
        var items = work as IReadOnlyList<Func<IServiceProvider, CancellationToken, Task<T>>> ?? work.ToList();
        if (items.Count == 0)
            return Array.Empty<T>();

        // Snapshot the PARENT tenant BEFORE fanning out. Each child scope's ITenantContext starts UNRESOLVED;
        // the EF global query filter is fail-open, so without this re-seed a child would see every tenant's data.
        var isSystem = _tenant.IsSystemContext;
        var tenantId = _tenant.TenantId;
        var subdomain = _tenant.Subdomain;
        var status = _tenant.Status;
        var plan = _tenant.Plan;
        var enabledModules = _tenant.EnabledModules;
        var logoUrl = _tenant.LogoUrl;
        var primaryColor = _tenant.PrimaryColor;

        var results = new T[items.Count];
        var dop = Math.Max(1, maxDegreeOfParallelism);
        using var gate = new SemaphoreSlim(dop, dop);

        var tasks = new Task[items.Count];
        for (int idx = 0; idx < items.Count; idx++)
        {
            int i = idx; // capture per-iteration index
            tasks[i] = RunOneAsync();

            async Task RunOneAsync()
            {
                await gate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    using var scope = _scopeFactory.CreateScope();

                    // THE GATE: re-seed the child scope's tenant context (fixes the scoped context AND the
                    // AsyncLocal ambient) before ANY query the factory issues. Do NOT remove — see class remarks.
                    var childTenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                    if (isSystem)
                        childTenant.SetSystemContext();
                    else
                        childTenant.SetTenant(tenantId, subdomain, status, plan, enabledModules, logoUrl, primaryColor);

                    results[i] = await items[i](scope.ServiceProvider, ct).ConfigureAwait(false);
                }
                finally
                {
                    gate.Release();
                }
            }
        }

        // Task.WhenAll rethrows the first inner exception directly when awaited. We still await ALL tasks so
        // every child scope settles (and disposes) before we surface a failure — no orphaned in-flight work.
        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}
