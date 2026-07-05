// ============================================================================
// US-PRF-002 / BUG-063: the DI container must bind ICyclePhaseScheduler to the
// concrete HangfireCyclePhaseScheduler. The Hangfire-backed phase-transition
// scheduling (US-PRF-004 FR-5) is dead unless the seam is actually registered:
// AppraisalCycleService takes ICyclePhaseScheduler as an OPTIONAL (nullable) ctor
// dependency, so a missing registration fails silently (scheduling is skipped)
// rather than erroring — exactly the class of bug this guard catches.
//
// PRE-FIX BEHAVIOUR (HEAD): no registration for ICyclePhaseScheduler in Program.cs
// → GetService<ICyclePhaseScheduler>() resolves null.
//
// POST-FIX BEHAVIOUR (under test): Program.cs registers
//   AddScoped<ICyclePhaseScheduler, HangfireCyclePhaseScheduler>()
// → the container resolves a non-null HangfireCyclePhaseScheduler.
//
// Hangfire scheduling itself is not unit-testable, so this is a registration guard
// that boots the real app's DI (WebApplicationFactory<Program>) and resolves the
// service — mirroring the other Hangfire scheduler seams wired the same way
// (IInterviewReminderScheduler, IOfferExpiryScheduler, IPayrollRunJobScheduler).
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using HRM.Application.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

[Collection("HttpApi")]
public sealed class CyclePhaseSchedulerRegistrationTests
{
    private readonly ApiTestFactory _factory;

    public CyclePhaseSchedulerRegistrationTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public void Di_ResolvesCyclePhaseScheduler_BUG063()
    {
        // Build the service provider from the app's real registration (Program.cs) and resolve the
        // scheduler within a scope (it is registered AddScoped, like its sibling scheduler seams).
        using var scope = _factory.Services.CreateScope();

        var scheduler = scope.ServiceProvider.GetService<ICyclePhaseScheduler>();

        scheduler.Should().NotBeNull(
            "ICyclePhaseScheduler must be registered so US-PRF-004 phase-transition jobs are actually scheduled");
        scheduler.Should().BeOfType<HangfireCyclePhaseScheduler>(
            "the Hangfire-backed implementation is the concrete binding");
    }
}
