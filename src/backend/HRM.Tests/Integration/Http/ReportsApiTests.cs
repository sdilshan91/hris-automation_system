using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration tests for the Reports &amp; Analytics module. Runs as <c>admin@hrm.local</c> on the
/// seeded <c>platform</c> tenant and exercises the genuine HTTP → controller → MediatR → Npgsql path against a
/// throwaway Postgres container.
///
/// <para>Reports has no "create entity" — the primary surface is read/generate. This asserts the report-type
/// catalog <c>GET /api/v1/reports</c> returns 200 with the expected descriptor shape (each item has a kebab
/// <c>type</c> and an <c>icon</c>), and that the primary <c>POST /api/v1/reports/headcount/generate</c> path
/// returns 200 with a report result over the tenant's (possibly empty) employee data. Gated by
/// <c>Reports.View</c>, which the platform admin holds.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class ReportsApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public ReportsApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReportCatalog_List_Returns200WithDescriptorShape()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        // GET /api/v1/reports returns ApiResponse<IReadOnlyList<HrReportDescriptorDto>> → bare data[].
        var list = await client.GetAsync("/api/v1/reports");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        data.ValueKind.Should().Be(JsonValueKind.Array);
        data.GetArrayLength().Should().BeGreaterThan(0, "the pre-built report catalog must list report types");

        // Each descriptor carries a kebab 'type' key and a Material 'icon' token (HrReportDescriptorDto).
        var types = data.EnumerateArray()
            .Select(e => e.GetProperty("type").GetString())
            .ToList();
        types.Should().Contain("headcount", "the headcount report is one of the six pre-built report types");
        data.EnumerateArray().First().TryGetProperty("icon", out _).Should().BeTrue();
    }

    [Fact]
    public async Task HeadcountReport_Generate_Returns200WithResult()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        // POST /api/v1/reports/{type}/generate accepts an optional HrReportQueryParams body (filters). An empty
        // body generates the unfiltered report. Returns ApiResponse<HrReportResult>.
        var generate = await client.PostAsJsonAsync(
            "/api/v1/reports/headcount/generate", new { });

        generate.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(generate));

        using var doc = JsonDocument.Parse(await generate.Content.ReadAsStringAsync());
        // The envelope carries a non-null report result; its inner shape is report-type specific.
        doc.RootElement.GetProperty("data").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    // ── helpers (mirrors CoreHrApiTests) ─────────────────────────────────

    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => $"Response body: {await response.Content.ReadAsStringAsync()}";
}
