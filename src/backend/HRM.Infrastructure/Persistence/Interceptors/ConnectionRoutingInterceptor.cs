using System.Data.Common;
using HRM.Infrastructure.Multitenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace HRM.Infrastructure.Persistence.Interceptors;

/// <summary>
/// P3/RLS increment 2b — routes the app's DB connection to the right database ROLE at connection-open time,
/// driven by the <see cref="AmbientTenant"/> (AsyncLocal) rather than the scoped <c>ITenantContext</c>.
///
/// <para><b>Two-connection model:</b> under RLS the runtime path connects as <c>hrm_app</c> (LOGIN,
/// NOBYPASSRLS) so the <c>app.current_tenant</c> GUC actually constrains it; privileged paths — startup /
/// migrations / seeding, the system-admin (cross-tenant) context, and any unresolved/no-context flow (e.g. a
/// no-context background job) — connect as <c>hrm_owner</c> (BYPASSRLS) so they can span tenants and own DDL.
/// <c>DefaultConnection</c> ⇒ hrm_app; <c>PrivilegedConnection</c> ⇒ hrm_owner.</para>
///
/// <para><b>Selector:</b> <c>usePrivileged = !(ambient is { IsResolved: true, IsSystemContext: false })</c> —
/// i.e. ONLY a resolved, non-system tenant uses the normal (hrm_app) connection; everything else (unresolved,
/// null, or system) uses the privileged (hrm_owner) connection. The ambient is used deliberately (not the
/// scoped tenant context): it flows down the async context and is therefore resolvable at connection-open time
/// uniformly for HTTP requests, Hangfire jobs, and startup — where no scope may exist.</para>
///
/// <para><b>Non-breaking today (increment 2b):</b> when <c>PrivilegedConnection</c> is null/blank (the committed
/// default), this ALWAYS uses <c>DefaultConnection</c> and never mutates the connection string — so behaviour is
/// identical to before this interceptor existed. Populating <c>PrivilegedConnection</c> (+ pointing
/// <c>DefaultConnection</c> at hrm_app and flipping <c>Rls:Enabled</c>) is the increment-3 flip.</para>
///
/// <para>Registered as a singleton — it holds only static configuration (the two connection strings), so it is
/// simpler and cheaper than a scoped selector. A <see cref="DbConnectionInterceptor"/> is independent of the
/// SaveChanges/command interceptors; when a per-request transaction is held (TenantTransactionBehavior) the
/// connection string is fixed for that transaction's lifetime — correct, because the GUC is set on that same
/// hrm_app connection.</para>
/// </summary>
public sealed class ConnectionRoutingInterceptor : DbConnectionInterceptor
{
    private readonly string? _defaultConnectionString;
    private readonly string? _privilegedConnectionString;

    public ConnectionRoutingInterceptor(IConfiguration configuration)
    {
        _defaultConnectionString = configuration.GetConnectionString("DefaultConnection");
        _privilegedConnectionString = configuration.GetConnectionString("PrivilegedConnection");
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        RouteConnection(connection);
        return base.ConnectionOpening(connection, eventData, result);
    }

    public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        RouteConnection(connection);
        return base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }

    /// <summary>
    /// Picks the target connection string from the ambient tenant and applies it to the connection about to be
    /// opened — but only when it actually differs, to avoid needless string churn / pool re-keying.
    /// <c>internal</c> so the routing decision is unit-testable directly (via <c>InternalsVisibleTo</c>) without
    /// constructing EF's <see cref="ConnectionEventData"/>.
    /// </summary>
    internal void RouteConnection(DbConnection connection)
    {
        // Blank privileged ⇒ always the default. This is what makes increment 2b non-breaking: with the
        // committed-blank PrivilegedConnection we never touch the connection string at all.
        if (string.IsNullOrWhiteSpace(_privilegedConnectionString))
        {
            return;
        }

        var target = SelectPrivileged() ? _privilegedConnectionString : _defaultConnectionString;

        if (!string.IsNullOrWhiteSpace(target) && connection.ConnectionString != target)
        {
            connection.ConnectionString = target;
        }
    }

    /// <summary>
    /// Privileged (hrm_owner) unless the ambient is a resolved, NON-system tenant. Resolved-non-system ⇒ normal
    /// (hrm_app); unresolved / null / system ⇒ privileged.
    /// </summary>
    private static bool SelectPrivileged() =>
        AmbientTenant.Current is not { IsResolved: true, IsSystemContext: false };
}
