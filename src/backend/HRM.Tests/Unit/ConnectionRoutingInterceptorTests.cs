// ============================================================================
// RLS increment 2b — unit tests for ConnectionRoutingInterceptor's role selector.
//
// Proves the pure routing decision (which connection string is applied at ConnectionOpening) for each ambient
// tenant state, WITHOUT a database.
//
// GAP-001 inverted this selector. It now reads: SYSTEM context ⇒ PrivilegedConnection (hrm_owner); everything
// else — resolved tenant, unresolved, null — ⇒ DefaultConnection (hrm_app). Blank-privileged ⇒ ALWAYS default
// (the non-breaking guarantee). Previously "unresolved" also meant privileged, which made forgetting to scope
// something the most dangerous thing you could do in this codebase.
// ============================================================================

using FluentAssertions;
using HRM.Infrastructure.Multitenancy;
using HRM.Infrastructure.Persistence.Interceptors;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace HRM.Tests.Unit;

[Trait("Category", "ConnectionRouting")]
public sealed class ConnectionRoutingInterceptorTests : IDisposable
{
    private const string DefaultHost = "default-host";
    private const string PrivilegedHost = "priv-host";
    private const string DefaultCs = $"Host={DefaultHost};Database=db;Username=hrm_app";
    private const string PrivilegedCs = $"Host={PrivilegedHost};Database=db;Username=hrm_owner";

    public void Dispose() => AmbientTenant.Clear(); // never let one test's ambient bleed into the next

    private static ConnectionRoutingInterceptor Build(string defaultCs, string? privilegedCs)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = defaultCs,
                ["ConnectionStrings:PrivilegedConnection"] = privilegedCs,
            })
            .Build();
        return new ConnectionRoutingInterceptor(config);
    }

    // Applies the interceptor's routing to a not-yet-opened connection and returns the Host that would be used —
    // the observable routing decision. Exercises RouteConnection, the same code ConnectionOpening[Async] calls.
    private static string RoutedHost(ConnectionRoutingInterceptor interceptor, string startingCs)
    {
        using var connection = new NpgsqlConnection(startingCs);
        interceptor.RouteConnection(connection);
        return new NpgsqlConnectionStringBuilder(connection.ConnectionString).Host!;
    }

    [Fact]
    public void ResolvedNonSystemTenant_RoutesToDefault_HrmApp()
    {
        var interceptor = Build(DefaultCs, PrivilegedCs);
        AmbientTenant.SetTenant(Guid.NewGuid());

        RoutedHost(interceptor, DefaultCs).Should().Be(DefaultHost);
    }

    [Fact]
    public void SystemContext_RoutesToPrivileged_HrmOwner()
    {
        var interceptor = Build(DefaultCs, PrivilegedCs);
        AmbientTenant.SetSystem();

        // Start from the default string to prove the interceptor actively swaps it to privileged.
        RoutedHost(interceptor, DefaultCs).Should().Be(PrivilegedHost);
    }

    [Fact]
    public void UnresolvedAmbient_Null_RoutesToDefault_HrmApp()
    {
        // GAP-001 — THIS ASSERTION IS DELIBERATELY THE INVERSE of what it was. It previously read
        // `Should().Be(PrivilegedHost)` and was correct about the code: an unresolved ambient really did select
        // the BYPASSRLS hrm_owner role. That was the fourth and deepest link of GAP-001, where the same
        // "nothing resolved" state also turned the EF query filters into tautologies and skipped the BUG-003
        // guard — four isolation layers sharing one off-switch.
        //
        // The specification changed, not the assertion's strictness: privilege must now be ASKED for
        // (CrossTenantScope / SetSystemContext), so "I forgot to scope this" fails closed instead of being
        // handed the most powerful role in the system.
        var interceptor = Build(DefaultCs, PrivilegedCs);
        AmbientTenant.Clear(); // no ambient established (a request that resolved no tenant)

        RoutedHost(interceptor, DefaultCs).Should().Be(DefaultHost,
            "absence of a tenant is not authority — unresolved must get the NOBYPASSRLS role");
    }

    [Fact]
    public void UnresolvedAmbient_StartingFromPrivileged_IsDowngradedToDefault()
    {
        // The direction that actually matters for pooling: connections are reused, so a connection LAST used by
        // a privileged path must be actively swapped back, not merely left alone. Starting from the privileged
        // string proves the interceptor downgrades rather than only ever upgrading.
        var interceptor = Build(DefaultCs, PrivilegedCs);
        AmbientTenant.Clear();

        RoutedHost(interceptor, PrivilegedCs).Should().Be(DefaultHost);
    }

    [Fact]
    public void AResolvedTenantIsNeverPrivileged_EvenStartingFromThePrivilegedString()
    {
        var interceptor = Build(DefaultCs, PrivilegedCs);
        AmbientTenant.SetTenant(Guid.NewGuid());

        RoutedHost(interceptor, PrivilegedCs).Should().Be(DefaultHost);
    }

    [Fact]
    public void BlankPrivileged_ResolvedTenant_AlwaysUsesDefault()
    {
        var interceptor = Build(DefaultCs, privilegedCs: "");
        AmbientTenant.SetTenant(Guid.NewGuid());

        RoutedHost(interceptor, DefaultCs).Should().Be(DefaultHost);
    }

    [Fact]
    public void BlankPrivileged_SystemContext_StillUsesDefault_NonBreaking()
    {
        // The critical non-breaking guarantee: even a privileged (system) path stays on DefaultConnection when
        // PrivilegedConnection is blank — nothing is swapped, so behaviour is identical to pre-2b.
        var interceptor = Build(DefaultCs, privilegedCs: null);
        AmbientTenant.SetSystem();

        RoutedHost(interceptor, DefaultCs).Should().Be(DefaultHost);
    }
}
