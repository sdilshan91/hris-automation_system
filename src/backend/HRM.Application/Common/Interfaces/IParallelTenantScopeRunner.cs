namespace HRM.Application.Common.Interfaces;

/// <summary>
/// DF-50 — runs a set of INDEPENDENT per-tenant work items CONCURRENTLY, each in its OWN DI scope with its own
/// scoped <see cref="ITenantContext"/> re-seeded to the parent's tenant. The scope-per-item design is the
/// concurrency mechanism (like <see cref="ITenantJobRunner"/> for jobs): each item gets a fresh scoped
/// <c>AppDbContext</c> + collaborators, so the caller can fan out over a single logical unit of work without
/// two operations racing on one shared <c>DbContext</c> (EF forbids that).
///
/// <para><b>Tenant isolation is the load-bearing invariant.</b> A brand-new child scope resolves an UNRESOLVED
/// <see cref="ITenantContext"/>; the EF global query filter is fail-open
/// (<c>!IsResolved || TenantId == …</c>), so an unresolved context would return EVERY tenant's rows — a silent
/// cross-tenant leak. The runner therefore captures the parent tenant up front and calls
/// <see cref="ITenantContext.SetTenant"/> on every child scope BEFORE the work runs (which also republishes the
/// AsyncLocal ambient so the RLS GUC + cache key-prefix stay correct). Removing that re-seed re-opens the leak.</para>
/// </summary>
public interface IParallelTenantScopeRunner
{
    /// <summary>
    /// Executes each factory in <paramref name="work"/> concurrently, each inside its own DI scope whose
    /// <see cref="ITenantContext"/> is re-seeded to the CURRENT (parent) tenant before the factory is invoked.
    /// Concurrency is bounded by <paramref name="maxDegreeOfParallelism"/>. Results are returned in the SAME
    /// order as the input factories. An exception thrown by any factory propagates (the first observed one is
    /// rethrown) after all in-flight items have settled — matching the caller's existing "a failure fails the
    /// whole unit" behaviour; there is no per-item swallowing here.
    /// </summary>
    /// <typeparam name="T">The per-item result type.</typeparam>
    /// <param name="work">The independent work items; each receives its CHILD scope's <see cref="IServiceProvider"/>.</param>
    /// <param name="maxDegreeOfParallelism">Max items running at once (floored to 1). Bound this to avoid pool exhaustion.</param>
    /// <param name="ct">Cancellation token flowed into every factory.</param>
    Task<IReadOnlyList<T>> RunAllAsync<T>(
        IEnumerable<Func<IServiceProvider, CancellationToken, Task<T>>> work,
        int maxDegreeOfParallelism,
        CancellationToken ct = default);
}
