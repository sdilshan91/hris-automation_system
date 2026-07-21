// ============================================================================
// DF-58 — DI registration guard for the two durability-backstop reconcile jobs.
//
// Program.cs schedules OnboardingOutboxReconcileJob + PayrollRunReconcileJob via
// recurringJobs.AddOrUpdate<T>(). Hangfire's AspNetCoreJobActivator resolves the concrete job type from the
// DI scope when the job fires — if it is not AddScoped-registered the activation fails at run time and the
// scheduled backstop is dead code. This boots the app's real DI (shared ApiTestFactory /
// WebApplicationFactory<Program>) and resolves both — mirroring ThemeLJobRegistrationTests. Booting also
// proves the new recurringJobs.AddOrUpdate registrations don't throw at startup.
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

[Collection("HttpApi")]
public sealed class Df58ReconcileJobRegistrationTests
{
    private readonly ApiTestFactory _factory;

    public Df58ReconcileJobRegistrationTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public void Di_ResolvesOnboardingOutboxReconcileJob()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetService<OnboardingOutboxReconcileJob>().Should().NotBeNull(
            "OnboardingOutboxReconcileJob must be AddScoped-registered so its DF-58 recurring schedule can " +
            "activate the job at run time");
    }

    [Fact]
    public void Di_ResolvesPayrollRunReconcileJob()
    {
        using var scope = _factory.Services.CreateScope();

        scope.ServiceProvider.GetService<PayrollRunReconcileJob>().Should().NotBeNull(
            "PayrollRunReconcileJob must be AddScoped-registered so its DF-58 recurring schedule can activate " +
            "the job at run time");
    }
}
