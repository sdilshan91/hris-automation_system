using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration tests for the Core HR module (departments, job titles, employees).
/// These run as <c>admin@hrm.local</c> on the seeded <c>platform</c> tenant and exercise the genuine
/// HTTP → controller → MediatR → Npgsql path against a throwaway Postgres container — catching
/// routing / model-binding / validation / auth / tenant-isolation contract bugs that the mocked
/// MediatR and EF-InMemory unit tests cannot see.
/// </summary>
[Collection("HttpApi")]
public sealed class CoreHrApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public CoreHrApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_AsSeededAdmin_ReturnsAccessToken()
    {
        // The factory helper throws if login fails, so reaching this assertion proves the token parse.
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        client.DefaultRequestHeaders.Authorization.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization!.Parameter.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Departments_CreateThenList_ReturnsCreatedDepartment()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        // CreateDepartmentRequest: { name, code, description?, parentDepartmentId?, managerId? }
        // code MUST match ^[A-Za-z0-9\-_]+$ (max 50); name is required (max 150).
        var create = await client.PostAsJsonAsync("/api/v1/tenant/departments", new
        {
            name = $"Engineering {suffix}",
            code = $"ENG-{suffix}",
            description = "Engineering department (integration test)",
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(create));
        var createdId = await ReadDataIdAsync(create);
        createdId.Should().NotBeEmpty();

        var list = await client.GetAsync("/api/v1/tenant/departments");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        var ids = await ReadDataIdsAsync(list);
        ids.Should().Contain(createdId, "the just-created department must appear in the tenant's list");
    }

    [Fact]
    public async Task JobTitles_CreateThenList_ReturnsCreatedJobTitle()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        // CreateJobTitleRequest: { titleName, description?, gradeId? }
        var create = await client.PostAsJsonAsync("/api/v1/tenant/job-titles", new
        {
            titleName = $"Software Engineer {suffix}",
            description = "IC engineering role (integration test)",
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(create));
        var createdId = await ReadDataIdAsync(create);
        createdId.Should().NotBeEmpty();

        var list = await client.GetAsync("/api/v1/tenant/job-titles");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        var ids = await ReadDataIdsAsync(list);
        ids.Should().Contain(createdId, "the just-created job title must appear in the tenant's list");
    }

    [Fact]
    public async Task Employees_CreateThenList_ReturnsCreatedEmployee()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        // Employees FK to a real department + job title — create those first.
        var deptResp = await client.PostAsJsonAsync("/api/v1/tenant/departments", new
        {
            name = $"People Ops {suffix}",
            code = $"POPS-{suffix}",
        });
        deptResp.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(deptResp));
        var departmentId = await ReadDataIdAsync(deptResp);

        var titleResp = await client.PostAsJsonAsync("/api/v1/tenant/job-titles", new
        {
            titleName = $"HR Generalist {suffix}",
        });
        titleResp.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(titleResp));
        var jobTitleId = await ReadDataIdAsync(titleResp);

        // CreateEmployeeRequest: firstName, lastName, email, dateOfJoining, departmentId, jobTitleId,
        // employmentType (enum string), status? must be Active|Probation. EmploymentType serializes as
        // its PascalCase name ("FullTime") per the app's JsonStringEnumConverter.
        var create = await client.PostAsJsonAsync("/api/v1/tenant/employees", new
        {
            firstName = "Ada",
            lastName = $"Lovelace {suffix}",
            email = $"ada.{suffix}@e2e-int.test",
            dateOfJoining = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            departmentId,
            jobTitleId,
            employmentType = "FullTime",
            status = "Active",
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(create));
        var createdId = await ReadDataIdAsync(create);
        createdId.Should().NotBeEmpty();

        // Employees list returns a paginated envelope: data.items[].
        var list = await client.GetAsync("/api/v1/tenant/employees?page=1&pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var ids = doc.RootElement
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();

        ids.Should().Contain(createdId, "the just-created employee must appear in the tenant's list");
    }

    // ── helpers ──────────────────────────────────────────────────────

    /// <summary>Short, code-safe unique suffix (matches the department-code charset).</summary>
    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => $"Response body: {await response.Content.ReadAsStringAsync()}";

    /// <summary>Reads <c>data.id</c> from an <c>ApiResponse&lt;T&gt;</c> envelope.</summary>
    private static async Task<Guid> ReadDataIdAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    /// <summary>Reads the <c>id</c> of every element from an <c>ApiResponse&lt;list&gt;</c> envelope.</summary>
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
