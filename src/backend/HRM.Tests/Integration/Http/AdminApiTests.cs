using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration tests for the Admin Console module (tenant roles + users). Runs as
/// <c>admin@hrm.local</c> on the seeded <c>platform</c> tenant and exercises the genuine HTTP → controller →
/// MediatR → Npgsql path against a throwaway Postgres container.
///
/// <para>Primary happy paths: <c>POST/GET /api/v1/tenant/roles</c> (create a custom role with a valid
/// permission, then confirm it appears in the role list) and <c>GET /api/v1/tenant/users</c> (the paginated
/// tenant-user list). Roles list returns <c>ApiResponse&lt;IReadOnlyList&lt;RoleDto&gt;&gt;</c> → a bare
/// <c>data[]</c> array; the users list returns <c>ApiResponse&lt;PagedResult&lt;...&gt;&gt;</c> →
/// <c>data.items[]</c>. Gated by <c>Roles.Manage</c>/<c>Roles.View</c> and <c>Tenant.ManageUsers</c>, which the
/// platform admin holds.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class AdminApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public AdminApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Roles_CreateThenList_ReturnsCreatedRole()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        // CreateRoleRequest: { name (req, <=100), description? (<=500), permissions[] (each must be a valid
        // PermissionCatalog key) }. "Roles.View" is a known-valid catalog permission.
        var create = await client.PostAsJsonAsync("/api/v1/tenant/roles", new
        {
            name = $"Reviewer {suffix}",
            description = "Custom read-only reviewer role (integration test)",
            permissions = new[] { "Roles.View" },
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(create));
        var createdId = await ReadDataIdAsync(create);
        createdId.Should().NotBeEmpty();

        // GET /api/v1/tenant/roles returns ApiResponse<IReadOnlyList<RoleDto>> → bare data[].
        var list = await client.GetAsync("/api/v1/tenant/roles");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        var ids = await ReadDataIdsAsync(list);
        ids.Should().Contain(createdId, "the just-created custom role must appear in the tenant's role list");
    }

    [Fact]
    public async Task Users_List_Returns200WithPagedItems()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        // GET /api/v1/tenant/users returns ApiResponse<PagedResult<TenantUserListItemDto>> → data.items[].
        var list = await client.GetAsync("/api/v1/tenant/users?page=1&pageSize=50");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var items = doc.RootElement.GetProperty("data").GetProperty("items");
        items.ValueKind.Should().Be(JsonValueKind.Array);
        // The seeded platform admin is itself a tenant user, so the list is non-empty.
        items.GetArrayLength().Should().BeGreaterThan(0, "the seeded admin membership must appear in the user list");
    }

    // ── helpers (mirrors CoreHrApiTests) ─────────────────────────────────

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => $"Response body: {await response.Content.ReadAsStringAsync()}";

    private static async Task<Guid> ReadDataIdAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task<List<Guid>> ReadDataIdsAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement
            .GetProperty("data")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();
    }
}
