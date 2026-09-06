// ============================================================================
// US-NTF-006 (delivery Phase 1) — DI feature-flag / fallback SAFETY property.
// Exercises the REAL Smtp:Host-gated swap in HRM.Infrastructure.DependencyInjection
// (AddInfrastructure): blank host → LogOnlyEmailSender (never a hard SMTP dependency,
// app starts + tests run with no SMTP server); non-blank host → SmtpEmailSender.
// Resolves IEmailSender from the container built by the ACTUAL registration code —
// it does NOT send anything.
//
// GAP-015 / G15 — the SAME registration additionally FAILS FAST when a blank Smtp:Host would select
// the log-only sender in Production: the app would accept password-reset and account-lockout requests
// and send NOTHING, and BR-1 designates that mail non-suppressible (US-NTF-006 FR-8). The guard reads
// the environment name from IConfiguration (ASPNETCORE_ENVIRONMENT, falling back to DOTNET_ENVIRONMENT),
// so both variables are exercised here — and so is the non-Production side, because a guard that is
// over-tightened into throwing on Staging or local dev is just as broken as one that never throws.
//
// Maps:  #1 blank host → LogOnlyEmailSender          #2 configured host → SmtpEmailSender
//        #3 blank host + Production → throws         #4 blank host + non-Production → LogOnlyEmailSender
//        #5 configured host + Production → SmtpEmailSender (the guard is about the SENDER, not the env)
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class EmailSenderDiRegistrationTests
{
    // The two keys the Production guard reads, in its own precedence order.
    public const string AspNetCoreEnvironmentKey = "ASPNETCORE_ENVIRONMENT";
    public const string DotNetEnvironmentKey = "DOTNET_ENVIRONMENT";

    // Build the container via the REAL AddInfrastructure so we test the actual gate, not a copy of it.
    // environmentKey/environmentName default to unset, which is what arms #1/#2 rely on.
    private static ServiceProvider BuildProvider(
        string? smtpHost,
        string? environmentKey = null,
        string? environmentName = null)
    {
        var settings = new Dictionary<string, string?>
        {
            // Registered lazily into the DbContext options and never opened here (we only resolve
            // IEmailSender), so a bare host placeholder with no credentials is enough.
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused",
            ["Smtp:Host"] = smtpHost,
        };

        if (environmentKey is not null)
        {
            settings[environmentKey] = environmentName;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
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

    // Both variables the guard consults, and the OrdinalIgnoreCase comparison it uses. A host that
    // publishes "production" must fail-fast exactly like one that publishes "Production".
    public static TheoryData<string, string> ProductionEnvironments => new()
    {
        { AspNetCoreEnvironmentKey, "Production" },
        { AspNetCoreEnvironmentKey, "production" },
        { DotNetEnvironmentKey, "Production" },
        { DotNetEnvironmentKey, "PRODUCTION" },
    };

    [Theory] // #3 — blank host in Production must FAIL FAST, not quietly wire a sender that delivers nothing
    [MemberData(nameof(ProductionEnvironments))]
    public void EmailSender_WhenSmtpHostBlank_InProduction_ThrowsAtRegistration(
        string environmentKey,
        string environmentName)
    {
        // The guard runs inside AddInfrastructure, so the failure is at REGISTRATION, not resolution:
        // startup dies before the app can accept a password-reset request it cannot fulfil.
        var act = () => BuildProvider(smtpHost: "", environmentKey, environmentName);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message
                // Name the real cause: the missing setting, the consequence, and the story it protects —
                // a bare "configuration error" would leave an operator guessing at 3am.
                .Should().Contain("Smtp:Host").And.Contain("Production")
                .And.Contain("password-reset").And.Contain("US-NTF-006");
    }

    // Everything that is NOT Production, including an environment name the host never published at all
    // (null). Dev/CI/Staging must keep booting on the log-only seam with no SMTP server available.
    public static TheoryData<string?, string?> NonProductionEnvironments => new()
    {
        { null, null },                                    // no environment variable published at all
        { AspNetCoreEnvironmentKey, "Development" },
        { AspNetCoreEnvironmentKey, "Staging" },
        { AspNetCoreEnvironmentKey, "" },
        { DotNetEnvironmentKey, "Development" },
        { DotNetEnvironmentKey, "Staging" },
    };

    [Theory] // #4 — the guard must not be over-tightened: a blank host outside Production still boots
    [MemberData(nameof(NonProductionEnvironments))]
    public void EmailSender_WhenSmtpHostBlank_OutsideProduction_ResolvesLogOnlyEmailSender(
        string? environmentKey,
        string? environmentName)
    {
        using var provider = BuildProvider(smtpHost: "", environmentKey, environmentName);
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        sender.Should().BeOfType<LogOnlyEmailSender>();
    }

    [Theory] // #5 — Production is not itself the fault condition: a configured host must NOT throw
    [InlineData(AspNetCoreEnvironmentKey)]
    [InlineData(DotNetEnvironmentKey)]
    public void EmailSender_WhenSmtpHostConfigured_InProduction_ResolvesSmtpEmailSenderWithoutThrowing(
        string environmentKey)
    {
        using var provider = BuildProvider(
            smtpHost: "smtp.example.test", environmentKey, environmentName: "Production");
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        sender.Should().BeOfType<SmtpEmailSender>();
    }

    /// <summary>
    /// ISSUE-447: the guard used to read <c>configuration["ASPNETCORE_ENVIRONMENT"]</c>. That key is only
    /// one of THREE ways an environment is expressed — the others being <c>--environment</c> and
    /// <c>IWebHostBuilder.UseEnvironment</c>, neither of which sets it. Worse, when nothing sets the
    /// environment at all, <see cref="IHostEnvironment.EnvironmentName"/> resolves to <b>Production</b>
    /// while the raw key returns null, so <c>string.Equals(null, "Production")</c> was false and the guard
    /// stayed silent.
    ///
    /// <para>That is a fail-open in precisely the deployment it exists to protect: a real production host
    /// with a blank <c>Smtp:Host</c> and no environment variable published would BOOT on
    /// <c>LogOnlyEmailSender</c> and silently swallow password-reset and account-lockout mail (BR-1 marks
    /// both non-suppressible). No existing arm covered it — every one supplies the variable explicitly,
    /// which is exactly the case that already worked.</para>
    /// </summary>
    [Fact] // #6 — resolved environment says Production while the raw variable says nothing
    public void EmailSender_WhenSmtpHostBlank_AndEnvironmentResolvesToProductionWithNoVariableSet_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused",
                ["Smtp:Host"] = "",
                // Deliberately NO ASPNETCORE_ENVIRONMENT / DOTNET_ENVIRONMENT key — this is the shape of a
                // host that expressed Production through UseEnvironment or by publishing nothing at all.
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Production);

        var act = () => services.AddInfrastructure(configuration, environment);

        act.Should().Throw<InvalidOperationException>(
                "a production host with no SMTP configured must fail fast rather than accept password-reset "
                + "requests it will silently drop — and the resolved environment is the only reading of "
                + "'am I in Production' that sees all three ways the host can express it")
            .Which.Message.Should().Contain("Smtp:Host").And.Contain("password-reset");
    }
}
