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
/// migrations / seeding and the system-admin (cross-tenant) context — connect as <c>hrm_owner</c> (BYPASSRLS)
/// so they can span tenants and own DDL. <c>DefaultConnection</c> ⇒ hrm_app; <c>PrivilegedConnection</c> ⇒
/// hrm_owner.</para>
///
/// <para><b>Selector (GAP-001):</b> <c>usePrivileged = ambient is { IsSystemContext: true }</c> — privilege is
/// granted ONLY to an explicitly-declared system context. Unresolved and null get the normal hrm_app connection.
/// This is the inverse of the original rule, which handed the BYPASSRLS role to anything that had not resolved a
/// tenant; see <c>SelectPrivileged</c> for why that made forgetting-to-scope the most dangerous default in the
/// system. The ambient is used deliberately (not the scoped tenant context): it flows down the async context and
/// is therefore resolvable at connection-open time uniformly for HTTP requests, Hangfire jobs, and startup —
/// where no scope may exist.</para>
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
    /// GAP-001 — privileged (hrm_owner) <b>only</b> when the ambient is an explicitly-declared SYSTEM context.
    /// Everything else, including unresolved and null, gets the normal NOBYPASSRLS <c>hrm_app</c> connection.
    ///
    /// <para><b>This used to be inverted</b> (<c>is not { IsResolved: true, IsSystemContext: false }</c>), so an
    /// unresolved context selected the BYPASSRLS role. That was the fourth and deepest link of GAP-001: an
    /// unresolved request simultaneously passed tenant resolution, turned the EF global query filters into
    /// tautologies (they read <c>!IsResolved || …</c>), skipped the BUG-003 access guard, AND landed on a
    /// connection that ignores RLS. Four independent isolation layers, one shared off-switch.</para>
    ///
    /// <para><b>Absence is not authority.</b> "Nothing resolved" and "this is deliberately cross-tenant" were
    /// indistinguishable, and the more dangerous reading was the default — so forgetting to scope something
    /// granted it the most powerful role in the system. Now privilege must be asked for
    /// (<see cref="CrossTenantScope"/> / <c>SetSystemContext()</c>), and the failure mode of forgetting is a
    /// query that returns nothing rather than one that returns everyone's data.</para>
    ///
    /// <para>Callers that legitimately run without a tenant and DO need privilege are explicit about it: startup
    /// migrations and seeding (<c>hrm_app</c> has DML but no DDL) enter a <see cref="CrossTenantScope"/>, and the
    /// cross-tenant Hangfire jobs declare <c>SetSystemContext()</c> — which GAP-024 made mechanically enforced
    /// rather than remembered.</para>
    /// </summary>
    private static bool SelectPrivileged() =>
        AmbientTenant.Current is { IsSystemContext: true };
}
