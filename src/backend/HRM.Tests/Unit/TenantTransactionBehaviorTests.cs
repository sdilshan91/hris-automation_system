using HRM.Application.Common.Interfaces;
using HRM.Infrastructure.Behaviors;
using HRM.Tests.Unit.Helpers;
using MediatR;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Xunit;

namespace HRM.Tests.Unit;

/// <summary>
/// US-PLT-002 — guards on the RLS transaction behavior. These prove the behavior is a
/// safe no-op on the EF InMemory provider (which supports neither transactions nor raw
/// SQL) and when the RLS flag/context says it should not engage. The ACTIVE path
/// (real SET LOCAL + transaction) is a Postgres-engine behavior verified by the Phase-4
/// CI integration tests, not here.
/// </summary>
public class TenantTransactionBehaviorTests
{
    public sealed record Ping(string Value) : IRequest<string>;

    private static IConfiguration Config(bool rlsEnabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Rls:Enabled"] = rlsEnabled ? "true" : "false",
            })
            .Build();

    private static ITenantContext Tenant(bool resolved, bool system)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.TenantId.Returns(Guid.NewGuid());
        ctx.IsResolved.Returns(resolved);
        ctx.IsSystemContext.Returns(system);
        return ctx;
    }

    private static async Task<(string result, bool nextCalled)> RunAsync(
        bool rlsEnabled, ITenantContext tenant)
    {
        using var db = TestDbContextFactory.Create(tenant);
        var behavior = new TenantTransactionBehavior<Ping, string>(db, tenant, Config(rlsEnabled));

        var nextCalled = false;
        RequestHandlerDelegate<string> next = _ =>
        {
            nextCalled = true;
            return Task.FromResult("handled");
        };

        var result = await behavior.Handle(new Ping("x"), next, CancellationToken.None);
        return (result, nextCalled);
    }

    [Fact]
    public async Task NoOps_when_rls_disabled()
    {
        var (result, nextCalled) = await RunAsync(rlsEnabled: false, Tenant(resolved: true, system: false));
        Assert.Equal("handled", result);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task NoOps_on_non_relational_provider_even_when_enabled()
    {
        // InMemory is not relational — the behavior must skip the transaction/SET LOCAL
        // (which would throw on InMemory) and simply invoke the handler.
        var (result, nextCalled) = await RunAsync(rlsEnabled: true, Tenant(resolved: true, system: false));
        Assert.Equal("handled", result);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task NoOps_for_system_context()
    {
        var (result, nextCalled) = await RunAsync(rlsEnabled: true, Tenant(resolved: true, system: true));
        Assert.Equal("handled", result);
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task NoOps_when_tenant_unresolved()
    {
        var (result, nextCalled) = await RunAsync(rlsEnabled: true, Tenant(resolved: false, system: false));
        Assert.Equal("handled", result);
        Assert.True(nextCalled);
    }
}
