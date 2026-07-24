using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// DF-enc-http-authz — real HTTP → <c>[RequirePermission("Tenant.Lifecycle")]</c> → controller proof for the
/// system encryption key-rotation endpoints (<c>POST/GET /api/v1/system/encryption/*</c>). These trigger /
/// report a FLEET-WIDE field re-encryption, so the only thing between read-only support staff and that trigger
/// is the <c>Tenant.Lifecycle</c> gate — which only the platform SystemAdmin holds; the read-only System
/// Support role holds <c>Tenant.ViewLifecycle</c> but NOT <c>Tenant.Lifecycle</c>.
///
/// <para>Positive is the read-only report only (GET → 200) — we deliberately do NOT POST a re-encrypt from an
/// authorized caller here, to avoid running a sweep on the shared "HttpApi" container. The negative POST
/// reaching a <c>403</c> already proves the route exists and the gate denies before the handler runs.</para>
/// </summary>
[Collection("HttpApi")]
[Trait("TC", "TC-PLT-007")] // encryption admin-endpoint authz: 403 for a non-SystemAdmin (DF-enc-http-authz)
public sealed class AdminEncryptionAuthorizationApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private const string ReencryptRoute = "/api/v1/system/encryption/reencrypt";
    private const string ReportRoute = "/api/v1/system/encryption/report";

    private readonly ApiTestFactory _factory;

    public AdminEncryptionAuthorizationApiTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Report_AsSystemAdmin_Returns200()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var response = await client.GetAsync(ReportRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the platform SystemAdmin holds Tenant.Lifecycle, so the gate lets it through and the route is wired: "
            + await BodyAsync(response));
    }

    // ── The guard: a read-only System-Support-equivalent caller (Tenant.ViewLifecycle, NOT Tenant.Lifecycle)
    //    is denied on BOTH the trigger and the report. A permission-less/wrong-permission caller reaching 403
    //    proves the [RequirePermission] filter runs BEFORE the handler (the route also exists → not a 404). ──

    [Fact]
    public async Task Reencrypt_AsSupportWithoutLifecyclePermission_Returns403()
    {
        var client = await _factory.CreateClientWithPermissionsAsync("Tenant.ViewLifecycle");

        var response = await client.PostAsync(ReencryptRoute, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "Tenant.ViewLifecycle must NOT be able to trigger a fleet-wide re-encryption — only Tenant.Lifecycle "
            + "(SystemAdmin) can: " + await BodyAsync(response));
    }

    [Fact]
    public async Task Report_AsSupportWithoutLifecyclePermission_Returns403()
    {
        var client = await _factory.CreateClientWithPermissionsAsync("Tenant.ViewLifecycle");

        var response = await client.GetAsync(ReportRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the key-usage report is gated by Tenant.Lifecycle, which System Support (ViewLifecycle only) lacks: "
            + await BodyAsync(response));
    }

    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => $"Response body: {await response.Content.ReadAsStringAsync()}";
}
