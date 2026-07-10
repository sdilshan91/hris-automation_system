// ============================================================================
// P3/RLS increment 2c — unit tests for TenantJobRunner (the per-tenant job GUC helper).
//
// These run on the EF InMemory provider (NOT relational), so they prove the NON-BREAKING behaviour that
// ships today: the runner ALWAYS establishes the tenant context (and publishes the AmbientTenant), and it
// NEVER opens a transaction / sets the GUC on a non-relational provider — regardless of the Rls:Enabled flag.
// The GUC-in-a-transaction path is a database-engine behaviour and is proved on real Postgres in
// RlsIsolationPostgresTests (see TenantJobRunner_OnAppRole_RlsEnabled_SeesOnlyItsTenant there).
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Infrastructure.Multitenancy;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.Extensions.Configuration;

namespace HRM.Tests.Unit;

[Trait("TC", "TC-PLT-002-RLS")]
[Trait("Category", "RlsJobRunner")]
public sealed class TenantJobRunnerTests
{
    private static IConfiguration Config(bool rlsEnabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Rls:Enabled"] = rlsEnabled ? "true" : "false" })
            .Build();

    private static (TenantJobRunner runner, TenantContext tenant, AppDbContext db) Build(bool rlsEnabled)
    {
        var tenant = new TenantContext();
        var db = TestDbContextFactory.Create(tenant);
        var runner = new TenantJobRunner(db, tenant, Config(rlsEnabled));
        return (runner, tenant, db);
    }

    [Fact]
    public async Task RunForTenantAsync_RlsDisabled_RunsWork_SetsTenant_NoTransaction()
    {
        var (runner, tenant, db) = Build(rlsEnabled: false);
        var tenantId = Guid.NewGuid();
        var ran = false;

        await runner.RunForTenantAsync(tenantId, "acme", _ =>
        {
            ran = true;
            // Tenant context is established for the work.
            tenant.TenantId.Should().Be(tenantId);
            tenant.IsResolved.Should().BeTrue();
            tenant.IsSystemContext.Should().BeFalse();
            // Non-breaking: no transaction is opened while RLS is off (and InMemory has none anyway).
            db.Database.CurrentTransaction.Should().BeNull();
            return Task.CompletedTask;
        });

        ran.Should().BeTrue();
        tenant.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task RunForTenantAsync_RlsEnabled_OnNonRelationalProvider_RunsWorkDirectly_NoTransaction()
    {
        // Even with the flag ON, the InMemory provider is not relational, so the runner must NOT try to open a
        // transaction / run raw set_config SQL (which InMemory cannot execute) — it runs the work directly.
        var (runner, tenant, db) = Build(rlsEnabled: true);
        var tenantId = Guid.NewGuid();
        var ran = false;

        await runner.RunForTenantAsync(tenantId, "acme", _ =>
        {
            ran = true;
            db.Database.CurrentTransaction.Should().BeNull();
            return Task.CompletedTask;
        });

        ran.Should().BeTrue();
        tenant.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task RunForTenantAsync_PublishesAmbientTenant_ForCacheAndRouting()
    {
        var (runner, _, _) = Build(rlsEnabled: false);
        var tenantId = Guid.NewGuid();
        AmbientTenantInfo? seen = null;

        await runner.RunForTenantAsync(tenantId, "acme", _ =>
        {
            // The AsyncLocal ambient is what the (root-singleton) cache key-prefix provider + connection router
            // read — it must reflect this tenant inside the work.
            seen = AmbientTenant.Current;
            return Task.CompletedTask;
        });

        seen.Should().NotBeNull();
        seen!.Value.TenantId.Should().Be(tenantId);
        seen.Value.IsResolved.Should().BeTrue();
        seen.Value.IsSystemContext.Should().BeFalse();
    }

    [Fact]
    public async Task RunForTenantAsync_PropagatesWorkExceptions()
    {
        var (runner, _, _) = Build(rlsEnabled: false);

        var act = async () => await runner.RunForTenantAsync(
            Guid.NewGuid(), "acme", _ => throw new InvalidOperationException("boom"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }

    [Fact]
    public async Task RunForTenantAsync_SecondCall_RescopesToNewTenant()
    {
        var (runner, tenant, _) = Build(rlsEnabled: false);
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        await runner.RunForTenantAsync(a, "a", _ => Task.CompletedTask);
        tenant.TenantId.Should().Be(a);

        await runner.RunForTenantAsync(b, "b", _ => Task.CompletedTask);
        tenant.TenantId.Should().Be(b);
    }
}
