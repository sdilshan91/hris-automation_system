// ============================================================================
// US-NTF-006 (delivery Phase 1) — DI feature-flag / fallback SAFETY property.
// Exercises the REAL Smtp:Host-gated swap in HRM.Infrastructure.DependencyInjection
// (AddInfrastructure): blank host → LogOnlyEmailSender (never a hard SMTP dependency,
// app starts + tests run with no SMTP server); non-blank host → SmtpEmailSender.
// Resolves IEmailSender from the container built by the ACTUAL registration code —
// it does NOT send anything.
//
// Maps:  #1 blank host → LogOnlyEmailSender   #2 configured host → SmtpEmailSender
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HRM.Tests.Unit;

public sealed class EmailSenderDiRegistrationTests
{
    // Build the container via the REAL AddInfrastructure so we test the actual gate, not a copy of it.
    private static ServiceProvider BuildProvider(string? smtpHost)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Registered lazily into the DbContext options and never opened here (we only resolve
                // IEmailSender), so a bare host placeholder with no credentials is enough.
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused",
                ["Smtp:Host"] = smtpHost,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();                    // LogOnly/Smtp senders need ILogger<T>
        services.AddInfrastructure(configuration); // the code under test performs the Smtp:Host gate
        return services.BuildServiceProvider();
    }

    [Fact] // #1 — blank host keeps the log-only stub (safe with no SMTP server)
    public void EmailSender_WhenSmtpHostBlank_ResolvesLogOnlyEmailSender()
    {
        using var provider = BuildProvider(smtpHost: "");
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        sender.Should().BeOfType<LogOnlyEmailSender>();
    }

    [Fact] // #2 — a configured host swaps in the real MailKit sender (resolved, not invoked)
    public void EmailSender_WhenSmtpHostConfigured_ResolvesSmtpEmailSender()
    {
        using var provider = BuildProvider(smtpHost: "smtp.example.test");
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        sender.Should().BeOfType<SmtpEmailSender>();
    }
}
