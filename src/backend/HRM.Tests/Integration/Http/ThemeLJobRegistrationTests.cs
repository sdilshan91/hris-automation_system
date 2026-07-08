// ============================================================================
// Phase 6 "Theme-L" cleanup — DI registration of two Hangfire recurring jobs.
//
// Program.cs schedules DocumentExpiryNotificationJob (US-CHR-008) and
// Feedback360ReminderJob (US-PRF-005) via recurringJobs.AddOrUpdate<T>(), but
// Hangfire's AspNetCoreJobActivator resolves the job type from the DI scope when
// the job fires. If the concrete type is not registered (AddScoped<T>()), the
// activation fails at run time — the scheduled job is effectively dead code.
//
// The Theme-L cleanup adds the two missing registrations:
//     builder.Services.AddScoped<DocumentExpiryNotificationJob>();
//     builder.Services.AddScoped<Feedback360ReminderJob>();
// mirroring the ~30 sibling AddScoped<...Job>() lines already in Program.cs.
//
// This is a registration guard: it boots the app's real DI (via the shared
// ApiTestFactory / WebApplicationFactory<Program>) and resolves both concrete
// job types from a scope — mirroring CyclePhaseSchedulerRegistrationTests, which
// guards the ICyclePhaseScheduler seam the same way. Pre-fix both resolve null;
// post-fix both resolve a non-null instance.
// ============================================================================

using FluentAssertions;
using HRM.Api.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

[Collection("HttpApi")]
public sealed class ThemeLJobRegistrationTests
{
    private readonly ApiTestFactory _factory;

    public ThemeLJobRegistrationTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public void Di_ResolvesDocumentExpiryNotificationJob()
    {
        using var scope = _factory.Services.CreateScope();

        var job = scope.ServiceProvider.GetService<DocumentExpiryNotificationJob>();

        job.Should().NotBeNull(
            "DocumentExpiryNotificationJob must be AddScoped-registered so its Hangfire recurring " +
            "schedule (Program.cs) can actually activate the job at run time");
    }

    [Fact]
    public void Di_ResolvesFeedback360ReminderJob()
    {
        using var scope = _factory.Services.CreateScope();

        var job = scope.ServiceProvider.GetService<Feedback360ReminderJob>();

        job.Should().NotBeNull(
            "Feedback360ReminderJob must be AddScoped-registered so its Hangfire recurring " +
            "schedule (Program.cs) can actually activate the job at run time");
    }
}
