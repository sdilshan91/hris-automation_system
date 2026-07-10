// ============================================================================
// RLS increment 2b — unit tests for ConnectionRoutingInterceptor's role selector.
//
// Proves the pure routing decision (which connection string is applied at ConnectionOpening) for each ambient
// tenant state, WITHOUT a database: resolved-non-system ⇒ DefaultConnection (hrm_app); system / unresolved /
// null ⇒ PrivilegedConnection (hrm_owner); and blank-privileged ⇒ ALWAYS default (the non-breaking guarantee).
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
    public void UnresolvedAmbient_Null_RoutesToPrivileged_HrmOwner()
    {
        var interceptor = Build(DefaultCs, PrivilegedCs);
        AmbientTenant.Clear(); // no ambient established (startup / no-context path)

        RoutedHost(interceptor, DefaultCs).Should().Be(PrivilegedHost);
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
