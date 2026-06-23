using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration tests for the Recruitment module (vacancies). Runs as <c>admin@hrm.local</c> on the
/// seeded <c>platform</c> tenant and exercises the genuine HTTP → controller → MediatR → Npgsql path against a
/// throwaway Postgres container.
///
/// <para>Primary happy path: <c>POST/GET /api/v1/recruitment/vacancies</c>. A vacancy is created in Draft
/// status; its FK fields (department, job title, location, hiring manager) are all optional at create time
/// (BR-2 only enforces them at publish), so no prerequisite entities are needed. The list endpoint returns the
/// FE contract <c>{ data, total, page, pageSize }</c> nested under the <c>ApiResponse</c> envelope, i.e.
/// <c>data.data[]</c>.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class RecruitmentApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public RecruitmentApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Vacancies_CreateThenList_ReturnsCreatedVacancy()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        // CreateVacancyRequest: { title (req, <=200), departmentId?, jobTitleId?, locationId?, hiringManagerId?,
        // employmentType (enum, default FullTime), headcount (>=1, default 1), salaryMin?, salaryMax?,
        // salaryCurrency? (3-letter), description (<=20000), qualifications?, applicationDeadline? (DateOnly),
        // publishToPublicCareers (default true) }. Only Title is required.
        var create = await client.PostAsJsonAsync("/api/v1/recruitment/vacancies", new
        {
            title = $"Backend Engineer {suffix}",
            employmentType = "FullTime",
            headcount = 2,
            description = "Build and maintain the HRM API (integration test).",
            salaryCurrency = "USD",
            salaryMin = 80000m,
            salaryMax = 120000m,
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(create));
        var createdId = await ReadDataIdAsync(create);
        createdId.Should().NotBeEmpty();

        // Canonical paged contract: data.items[] (PagedResult.Items nested under the ApiResponse envelope).
        var list = await client.GetAsync("/api/v1/recruitment/vacancies?page=1&pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var ids = doc.RootElement
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();

        ids.Should().Contain(createdId, "the just-created vacancy must appear in the tenant's list");
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
}
