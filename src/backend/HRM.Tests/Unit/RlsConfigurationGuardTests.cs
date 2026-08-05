// ============================================================================
// Wave 5 readiness — the HALF-FLIP guard.
//
// `Rls:Enabled` and `ConnectionStrings:PrivilegedConnection` are one decision expressed as two keys. Turn on
// RLS while the privileged connection is still blank and ConnectionRoutingInterceptor stays inert, so
// migrations, the RLS reconciler's own ALTER TABLE … FORCE, and Hangfire's schema bootstrap all run as the
// UNPRIVILEGED runtime role — a half-configured database where some statements are permission-denied and
// others applied.
//
// Nothing else in the pipeline notices the two keys disagreeing. These arms pin the refusal.
// ============================================================================

using FluentAssertions;
using HRM.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace HRM.Tests.Unit;

public sealed class RlsConfigurationGuardTests
{
    private static IServiceCollection Build(bool rlsEnabled, string? privilegedConnection)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused",
            ["Encryption:ActiveKeyId"] = "k1",
            ["Encryption:Keys:k1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Rls:Enabled"] = rlsEnabled ? "true" : "false",
        };

        if (privilegedConnection is not null)
            settings["ConnectionStrings:PrivilegedConnection"] = privilegedConnection;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddInfrastructure(configuration);
        return services;
    }

    [Fact]
    public void RLS_ON_with_a_BLANK_privileged_connection_refuses_to_start()
    {
        // Refusing is deliberately harsher than degrading to RLS-off. A silent downgrade would leave an
        // operator believing tenant isolation is ON while it is OFF — worse than an outage, because a service
        // that came up clean is a service nobody investigates.
        var act = () => Build(rlsEnabled: true, privilegedConnection: null);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PrivilegedConnection*",
                "the error must name the setting that is missing, not just say RLS is misconfigured");
    }

    [Fact]
    public void RLS_ON_with_a_WHITESPACE_privileged_connection_also_refuses()
    {
        // An empty-string or spaces value is what a half-filled deployment template actually produces —
        // ConnectionRoutingInterceptor treats it exactly like absent, so the guard must too.
        var act = () => Build(rlsEnabled: true, privilegedConnection: "   ");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RLS_ON_with_a_privileged_connection_configured_starts()
    {
        var act = () => Build(
            rlsEnabled: true, privilegedConnection: "Host=localhost;Database=unused;Username=owner");

        act.Should().NotThrow("this is the fully-configured target state of the flip");
    }

    [Fact]
    public void The_COMMITTED_DEFAULT_still_starts()
    {
        // RLS off + blank privileged is what is in appsettings.json today and what every test host uses. If the
        // guard caught this, it would break the entire suite rather than the misconfiguration it targets.
        var act = () => Build(rlsEnabled: false, privilegedConnection: null);

        act.Should().NotThrow();
    }
}
