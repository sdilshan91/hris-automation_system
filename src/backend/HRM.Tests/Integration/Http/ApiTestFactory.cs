using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration-test harness for the HRM API. Boots the actual ASP.NET Core pipeline
/// (<see cref="WebApplicationFactory{TEntryPoint}"/> over <c>Program</c>) against a throwaway
/// PostgreSQL container, so tests exercise the genuine HTTP → controller → MediatR → Npgsql path
/// (routing, model binding, validation, auth, tenant isolation) that mocked/InMemory tests miss.
///
/// Lifecycle: the Postgres container starts in <see cref="InitializeAsync"/> and is torn down in
/// <see cref="DisposeAsync"/>. The app runs in the Development environment so DbInitializer applies
/// migrations + seeds the platform admin (and the DEV-only E2E tenant), and so tenant resolution's
/// dev <c>X-Tenant-Subdomain</c> header fallback is active.
/// </summary>
public sealed class ApiTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    // Generated once per factory at runtime so NO private key is ever written to disk. Both the
    // signing and validation sides of JwtService read this same PEM from config, so tokens minted
    // during login validate on subsequent requests.
    private readonly string _jwtPrivateKeyPem = RSA.Create(2048).ExportPkcs8PrivateKeyPem();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development so: (1) DbInitializer migrate/seed failures don't fail-fast, (2) the DEV-only
        // E2E seed runs, (3) the dev X-Tenant-Subdomain header fallback in TenantResolutionMiddleware
        // is honoured (subdomain-based resolution can't work against the in-memory test server host).
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Throwaway Postgres container — backs both EF and Hangfire storage.
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                // Empty → SignalR uses the in-memory backplane; no Redis needed for tests.
                ["ConnectionStrings:Redis"] = "",
                // Runtime-generated RSA key (appsettings ships this blank). Not persisted anywhere.
                ["Jwt:PrivateKey"] = _jwtPrivateKeyPem,
                // Keep the Hangfire client/storage but never start the background worker loop — it
                // only adds polling noise and races the test DB lifecycle. (Gated in Program.cs.)
                ["Hangfire:DisableServer"] = "true",
            });
        });
    }

    /// <summary>
    /// Logs in as the given user on the given tenant subdomain and returns an <see cref="HttpClient"/>
    /// preconfigured with both the <c>Authorization: Bearer</c> header and the <c>X-Tenant-Subdomain</c>
    /// header, ready for tenant-scoped calls.
    /// </summary>
    public async Task<HttpClient> CreateAuthedClientAsync(string subdomain, string email, string password)
    {
        var client = CreateClient();

        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        loginRequest.Headers.Add("X-Tenant-Subdomain", subdomain);

        var response = await client.SendAsync(loginRequest);
        var raw = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Login failed for {email}@{subdomain}: {(int)response.StatusCode} {response.StatusCode}. Body: {raw}");
        }

        using var doc = JsonDocument.Parse(raw);
        var accessToken = doc.RootElement
            .GetProperty("data")
            .GetProperty("accessToken")
            .GetString();

        if (string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException($"Login response contained no accessToken. Body: {raw}");
        }

        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        client.DefaultRequestHeaders.Add("X-Tenant-Subdomain", subdomain);

        return client;
    }

    /// <summary>Shared JSON options matching the API's camelCase Web defaults.</summary>
    public static JsonSerializerOptions Json => JsonOptions;
}
