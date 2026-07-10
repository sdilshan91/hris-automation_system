using System.Net;
using FluentAssertions;
using Xunit;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration coverage for the P3 infra health probes (<c>/health/live</c>, <c>/health/ready</c>).
/// Boots the genuine ASP.NET Core pipeline over a throwaway Postgres Testcontainer, so:
/// <list type="bullet">
///   <item><b>/health/live</b> ⇒ 200 (process is up — no external deps).</item>
///   <item><b>/health/ready</b> ⇒ 200 when the DB container is up (readiness = Postgres reachable; Redis is
///   skipped because the test harness sets a blank Redis connection string).</item>
///   <item>both probes answer <b>anonymously</b> (no JWT / no X-Tenant-Subdomain header) — they must not sit
///   behind auth or tenant resolution.</item>
/// </list>
/// OTel span export is intentionally NOT asserted (no collector in tests); a clean startup + a passing probe
/// is the gate that the observability + health wiring is safe with a blank OTLP endpoint.
/// </summary>
[Collection("HttpApi")]
public sealed class HealthCheckApiTests
{
    private readonly ApiTestFactory _factory;

    public HealthCheckApiTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    [Trait("TC", "TC-INFRA-HEALTH")]
    public async Task HealthLive_ReturnsOk_WithoutAuth()
    {
        // A bare client: no Authorization header, no X-Tenant-Subdomain header.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the liveness probe is an anonymous infra endpoint that only reports the process is up");
    }

    [Fact]
    [Trait("TC", "TC-INFRA-HEALTH")]
    public async Task HealthReady_ReturnsOk_WhenDatabaseIsUp()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "readiness must be healthy when the Postgres container is up (Redis is skipped — blank conn string)");
    }
}
