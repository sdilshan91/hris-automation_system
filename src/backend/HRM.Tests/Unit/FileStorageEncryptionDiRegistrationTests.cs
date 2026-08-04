// ============================================================================
// ISSUE-359 — DI wiring guard for encryption-at-rest.
//
// WHY THIS EXISTS. Every arm in EncryptingFileStorageTests constructs
// `new EncryptingFileStorage(...)` by hand, because that is how you assert on the bytes that reach storage.
// But the thing that actually ships is the ONE line in `AddInfrastructure` that wraps LocalFileStorage in the
// decorator. Revert that line to `return storage;` and the entire 16-arm suite stays green while every payslip,
// employee document and offer letter goes back to landing on disk as plaintext — the exact finding ISSUE-359
// was raised for, silently reintroduced with no test failing.
//
// That is the failure mode this file exists to make impossible. Resolution alone is the assertion: nothing is
// uploaded, no file is touched, no connection is opened.
//
// Pattern mirrors LeaveYearResolverDiRegistrationTests / EmailSenderDiRegistrationTests.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Infrastructure;
using HRM.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace HRM.Tests.Unit;

public sealed class FileStorageEncryptionDiRegistrationTests
{
    private static ServiceProvider BuildProvider(string? fileEncryptionEnabled = null)
    {
        var settings = new Dictionary<string, string?>
        {
            // Lazily bound into the DbContext options and never opened — we only resolve services.
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused;Username=unused",
            // AddInfrastructure fail-fasts without a key ring (P3-4 PII-at-rest); nothing is encrypted here.
            ["Encryption:ActiveKeyId"] = "k1",
            ["Encryption:Keys:k1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };

        // Deliberately ABSENT by default, so the arms below prove the DEFAULT is encrypted-on rather than
        // proving that an explicit opt-in works.
        if (fileEncryptionEnabled is not null)
            settings["FileEncryption:Enabled"] = fileEncryptionEnabled;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddInfrastructure(configuration); // the code under test
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// THE arm that matters. With no FileEncryption config at all, what production injects into all 18
    /// consuming services must already be the encrypting decorator — not bare LocalFileStorage.
    /// </summary>
    [Fact]
    public void IFileStorage_resolves_to_the_ENCRYPTING_decorator_by_default_ISSUE359()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IFileStorage>()
            .Should().BeOfType<EncryptingFileStorage>(
                "encryption at rest must be the default a consumer gets, not something a deployment opts into — "
                + "if this resolves to LocalFileStorage, ISSUE-359 is back in production and no other test notices");
    }

    /// <summary>
    /// The kill-switch must still be reachable. It is write-only by design (reads always decrypt), so an
    /// operator flipping it off keeps the decorator in place rather than unwiring it — otherwise every
    /// already-sealed file would become unreadable the moment the switch was thrown.
    /// </summary>
    [Fact]
    public void The_kill_switch_keeps_the_decorator_wired_so_sealed_files_stay_readable_ISSUE359()
    {
        using var provider = BuildProvider(fileEncryptionEnabled: "false");
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IFileStorage>()
            .Should().BeOfType<EncryptingFileStorage>(
                "disabling NEW encryption must never remove the read path that opens existing sealed files");
    }

    /// <summary>The back-fill surface the admin endpoints dispatch to must resolve from the real container.</summary>
    [Fact]
    public void The_encryption_maintenance_service_is_registered_ISSUE359()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IFileEncryptionMaintenanceService>()
            .Should().BeOfType<FileEncryptionMaintenanceService>();
    }
}
