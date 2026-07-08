using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration test for the completed-cycle picker route (BUG-244 #6):
/// <c>GET /api/v1/tenant/performance/recommendations/cycles/completed</c>, gated by
/// <c>[RequirePermission("Performance.Publish.All", "Performance.Review.Team")]</c>.
///
/// <para>Runs as <c>admin@hrm.local</c> on the seeded <c>platform</c> tenant — the same persona
/// <see cref="PerformanceApiTests"/> uses for the Performance module. The platform admin (SystemAdmin) holds
/// <c>Performance.Publish.All</c>, so this proves the WITH-permission side of the gate (200, and never
/// Forbidden) AND that the route is actually wired through controller → MediatR → query handler. The response
/// is <c>ApiResponse&lt;IReadOnlyList&lt;CompletedCycleOptionDto&gt;&gt;</c> → a bare <c>data[]</c> array (may
/// legitimately be empty on a fresh tenant with no completed cycles — the gate + wiring are what's under test).</para>
///
/// <para>The 403 (WITHOUT-permission) side is covered declaratively by the <c>[RequirePermission]</c> attribute
/// (the same attribute enforced across the whole recommendation controller); no permissionless persona is
/// seeded with login credentials in the test container, so a negative HTTP arm here would be fabricated rather
/// than real. The status-filter behaviour (only Completed cycles) is asserted directly in
/// <c>RecommendationCompletedCyclesTests</c>.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class RecommendationCompletedCyclesApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public RecommendationCompletedCyclesApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CompletedCycles_WithPublishPermission_ReachesRoute_200()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var response = await client.GetAsync("/api/v1/tenant/performance/recommendations/cycles/completed");

        var body = await response.Content.ReadAsStringAsync();

        // The permission gate must ADMIT the authorized persona — never Forbidden — and the route must resolve.
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        // Shape check: ApiResponse<IReadOnlyList<CompletedCycleOptionDto>> → a `data` array (possibly empty).
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Array);
    }
}
