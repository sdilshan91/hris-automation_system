using System.Net;
using FluentAssertions;
using Xunit;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP negative-authorization regression for <b>ISSUE-255</b>: before this the "HttpApi" Testcontainers
/// harness only seeded high-privilege personas, so a genuine "403 without permission" arm for a
/// <c>[RequirePermission]</c>-gated route could not be written end-to-end — only positive 200 arms were
/// testable. <see cref="ApiTestFactory.CreateClientWithPermissionsAsync"/> now seeds a genuinely
/// permission-less persona (a tenant-scoped role with an EMPTY permission set) and returns an authed client,
/// so the <c>[RequirePermission]</c> gate can be proven to actually deny.
///
/// <para>Route under test: <c>GET /api/v1/tenant/performance/cycles</c> (<c>CyclesController.List</c>), gated on
/// the HR-only <c>Performance.SetGoal.All</c> / <c>Performance.Publish.All</c>. The two arms prove the gate
/// <b>discriminates</b> rather than always-allowing or always-denying:</para>
/// <list type="bullet">
///   <item><b>Permission-less persona</b> — a real, logged-in user whose role carries zero permissions must be
///   <b>403 Forbidden</b>. (The arm ISSUE-255 says was previously impossible to write.)</item>
///   <item><b>Privileged persona</b> — a user whose role carries <c>Performance.SetGoal.All</c> must clear the
///   gate: <b>200 OK</b> (the list is empty for the fresh tenant, which is still a 200).</item>
/// </list>
/// </summary>
[Collection("HttpApi")]
public sealed class PermissionlessAuthorizationApiTests
{
    private const string CyclesListRoute = "/api/v1/tenant/performance/cycles";
    private const string ManageCyclesPermission = "Performance.SetGoal.All";

    private readonly ApiTestFactory _factory;

    public PermissionlessAuthorizationApiTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    [Trait("TC", "TC-PRF-255")]
    public async Task PermissionlessPersona_IsForbidden_OnPermissionGatedRoute()
    {
        // A real, authenticated caller whose tenant role has an EMPTY permission set.
        var client = await _factory.CreateClientWithPermissionsAsync();

        var response = await client.GetAsync(CyclesListRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a logged-in user whose role carries no permissions must be denied by the [RequirePermission] gate (ISSUE-255)");
    }

    [Fact]
    [Trait("TC", "TC-PRF-255")]
    public async Task PrivilegedPersona_IsAllowed_OnSameRoute()
    {
        // Same route, but a role that DOES hold the gating permission — proves the gate discriminates
        // (the permission-less 403 above is a genuine denial, not an always-403).
        var client = await _factory.CreateClientWithPermissionsAsync(ManageCyclesPermission);

        var response = await client.GetAsync(CyclesListRoute);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden,
            "a role holding Performance.SetGoal.All must clear the [RequirePermission] gate");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
