// ============================================================================
// CAL-8 / ISSUE-305 — DI wiring guard for ITenantLeaveYearResolver.
//
// WHY THIS EXISTS. ISSUE-305 was a wiring bug: Tenant.FiscalYearStartMonth existed, was settable in the UI,
// and was read by nothing. The fix routes every leave-year label through ONE injected resolver — which means
// `DependencyInjection.AddInfrastructure` is now load-bearing for correct pay. The resolver is a REQUIRED
// ctor param precisely so a missing wire cannot silently fall back to the calendar year... but "required"
// converts the failure from *silently wrong* to *runtime throw*, and a throw at 02:00 in a Hangfire sweep is
// still a bad way to find out.
//
// Every leave fixture hand-rolls its own ServiceCollection, so NOTHING exercised the real registration: delete
// the AddScoped line and the whole suite stays green while production 500s on the first leave request. These
// arms resolve the consumers from the container built by the ACTUAL registration code.
//
// Pattern mirrors EmailSenderDiRegistrationTests / RedisWiringDiRegistrationTests: nothing is invoked, no
// connection is opened — resolution alone is the assertion.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace HRM.Tests.Unit;

public sealed class LeaveYearResolverDiRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Lazily bound into the DbContext options and never opened — we only resolve services.
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused",
                // AddInfrastructure fail-fasts without a key ring (P3-4 PII-at-rest); nothing is encrypted here.
                ["Encryption:ActiveKeyId"] = "k1",
                ["Encryption:Keys:k1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddInfrastructure(configuration); // the code under test
        return services.BuildServiceProvider();
    }

    /// <summary>The resolver itself must be registered — the single reader of Tenant.FiscalYearStartMonth.</summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void TenantLeaveYearResolver_IsRegistered()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantLeaveYearResolver>()
            .Should().BeOfType<TenantLeaveYearResolver>();
    }

    /// <summary>
    /// THE arm that matters: every consumer that takes the resolver as a REQUIRED ctor param must actually
    /// resolve from the real container. If `AddScoped&lt;ITenantLeaveYearResolver, TenantLeaveYearResolver&gt;()`
    /// is ever deleted or reordered out, each of these throws here — at build time in CI, rather than in a
    /// tenant's leave request or, worse, a payroll run.
    ///
    /// <para>Scope note: <c>ILeaveRequestService</c>/<c>ILopService</c> are NOT listed. They depend on
    /// <c>INotificationService</c> → <c>SignalRNotificationService</c>, which lives in HRM.Api and is
    /// registered in <c>Program.cs</c>, so an Infrastructure-only container cannot build them — and stubbing
    /// that in would test the stub, not the wiring. Their resolver dependency is instead covered by the
    /// FiscalLeaveYearIntegrationTests arms that construct them for real.</para>
    /// </summary>
    [Theory]
    [Trait("TC", "TC-LV-264")]
    [InlineData(typeof(ILeaveEntitlementService))]    // ledger CREDIT (accrual/pro-rata)
    [InlineData(typeof(ILeaveCarryForwardService))]   // ledger CREDIT (year-end)
    [InlineData(typeof(ILeaveDashboardService))]      // read
    [InlineData(typeof(ILeaveEncashmentService))]     // MONEY
    [InlineData(typeof(IPayrollFnFIntegration))]      // MONEY (final settlement)
    public void EveryLeaveYearConsumer_ResolvesFromTheRealContainer(Type serviceType)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService(serviceType);

        resolved.Should().NotBeNull(
            $"{serviceType.Name} takes ITenantLeaveYearResolver as a required dependency — if this throws, the "
            + "registration is missing and every fiscal-tenant leave/payroll path fails at runtime");
    }

    /// <summary>
    /// Scoped, not singleton: the resolver reads the AMBIENT tenant via ITenantContext and memoizes per tenant
    /// id. A singleton would outlive the request/job scope and hand one tenant's leave year to another's
    /// ledger — the memo is keyed to survive that, but the lifetime must not invite it (Critical Rule #1).
    /// </summary>
    [Fact]
    [Trait("TC", "TC-LV-264")]
    public void Resolver_IsScoped_NotSingleton()
    {
        using var provider = BuildProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var a = scopeA.ServiceProvider.GetRequiredService<ITenantLeaveYearResolver>();
        var b = scopeB.ServiceProvider.GetRequiredService<ITenantLeaveYearResolver>();

        a.Should().NotBeSameAs(b, "a per-tenant memo must not be shared across scopes");
        scopeA.ServiceProvider.GetRequiredService<ITenantLeaveYearResolver>()
            .Should().BeSameAs(a, "but within one scope it must be reused, or the memo buys nothing");
    }
}
