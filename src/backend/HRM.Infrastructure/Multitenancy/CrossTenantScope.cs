namespace HRM.Infrastructure.Multitenancy;

/// <summary>
/// Runs a block of genuinely CROSS-TENANT work on the privileged connection, then restores the ambient tenant.
///
/// <para><b>Why this is needed at all.</b> The EF global query filter is <b>fail-open</b>
/// (<c>!IsResolved || TenantId == current</c>), so <c>IgnoreQueryFilters()</c> has always been sufficient to
/// reach across tenants. Postgres RLS is <b>fail-closed</b>: the same call returns <b>zero rows</b> instead.
/// Every cross-tenant query written against the fail-open assumption therefore inverts its meaning the moment
/// <c>Rls:Enabled</c> flips — silently, with no error, and looking exactly like "no such record".</para>
///
/// <para>Both <c>ConnectionRoutingInterceptor</c> and <c>TenantGucConnectionInterceptor</c> read
/// <see cref="AmbientTenant.Current"/>, so entering system context here routes the next connection to
/// <c>hrm_owner</c> (BYPASSRLS) and blanks the GUC — which is what makes a legitimately cross-tenant read or
/// write work under RLS.</para>
///
/// <para><b>Restoring is the whole point.</b> Calling <see cref="AmbientTenant.SetSystem"/> directly would
/// leave the remainder of the request running as system context — i.e. permanently privileged, with tenant
/// isolation off for everything that followed. That is a far worse bug than the one being fixed, so the scope
/// is a <see cref="IDisposable"/> and never a bare call.</para>
///
/// <para><b>Limitation — open connections.</b> Routing and the GUC are applied when a connection is OPENED. If
/// the caller already holds an open connection (an explicit transaction, or an <c>ExecutionStrategy</c> block),
/// entering this scope will NOT re-route that connection. Use it around self-contained reads/writes, not inside
/// an already-started transaction.</para>
///
/// <para>Use sparingly and only where the operation is cross-tenant BY DESIGN — tenant switching, listing a
/// user's workspaces, revoking a user's sessions everywhere. It is a deliberate hole in tenant isolation; every
/// call site should be able to say why it is one.</para>
/// </summary>
public static class CrossTenantScope
{
    /// <summary>Enters system (cross-tenant) context until the returned handle is disposed.</summary>
    public static IDisposable Enter()
    {
        var previous = AmbientTenant.Current;
        AmbientTenant.SetSystem();
        return new Restorer(previous);
    }

    private sealed class Restorer : IDisposable
    {
        private readonly AmbientTenantInfo? _previous;
        private bool _disposed;

        public Restorer(AmbientTenantInfo? previous) => _previous = previous;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            AmbientTenant.Restore(_previous);
        }
    }
}
