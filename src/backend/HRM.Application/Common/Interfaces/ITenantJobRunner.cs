namespace HRM.Application.Common.Interfaces;

/// <summary>
/// P3/RLS increment 2c — the shared "run this per-tenant background work under the right tenant scope" helper
/// for Hangfire jobs.
///
/// <para><b>Why it exists:</b> jobs do not flow through MediatR, so <c>TenantTransactionBehavior</c> never sets
/// their <c>app.current_tenant</c> GUC. Under RLS a per-tenant job running on the <c>hrm_app</c> (NOBYPASSRLS)
/// connection with no GUC would be fail-closed — it would see NOTHING. This runner is the job-side equivalent of
/// that pipeline behavior: it establishes the tenant on the scoped <see cref="ITenantContext"/> (which also
/// publishes the <c>AmbientTenant</c> → the connection router picks <c>hrm_app</c> and the EF query filter +
/// cache prefix scope to the tenant) and, when RLS is enabled, wraps the work in a retry-safe transaction that
/// sets the GUC.</para>
///
/// <para><b>Non-breaking today:</b> the GUC/transaction is GATED on <c>Rls:Enabled</c> (false today). While the
/// flag is off the runner simply sets the tenant context and runs the work directly — no transaction, no raw SQL
/// — so behaviour is identical to the bare <c>SetTenant</c> + body it replaces.</para>
/// </summary>
public interface ITenantJobRunner
{
    /// <summary>
    /// Establishes the given tenant as the current context and runs <paramref name="work"/> under it. When
    /// <c>Rls:Enabled</c> is true (and the provider is relational) the work runs inside a retry-safe transaction
    /// that sets the <c>app.current_tenant</c> GUC; otherwise the work runs directly.
    ///
    /// <para>The <paramref name="work"/> delegate is the job's per-tenant body. Under RLS-on it must NOT open its
    /// own transaction (it would nest inside the runner's transaction) — resolve the per-tenant services from the
    /// same DI scope as this runner so their <see cref="ITenantContext"/> / DbContext are the ones this runner
    /// scopes and enlists.</para>
    /// </summary>
    /// <param name="tenantId">The tenant whose data the work operates on.</param>
    /// <param name="subdomain">A subdomain label for the tenant context (log/diagnostic only).</param>
    /// <param name="work">The per-tenant body, executed once the tenant context (and, under RLS, the GUC) is set.</param>
    /// <param name="cancellationToken">Cancellation token flowed into the work and any transaction.</param>
    Task RunForTenantAsync(
        Guid tenantId,
        string subdomain,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default);
}
