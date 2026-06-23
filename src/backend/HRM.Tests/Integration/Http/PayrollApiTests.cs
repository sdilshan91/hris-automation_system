using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration tests for the Payroll module (salary components + structures). Runs as
/// <c>admin@hrm.local</c> on the seeded <c>platform</c> tenant and exercises the genuine HTTP → controller →
/// MediatR → Npgsql path against a throwaway Postgres container.
///
/// <para>Primary happy path: <c>POST/GET /api/v1/payroll/salary-components</c>. A second test creates a salary
/// structure that links the just-created component (the structure's only real FK). Both list endpoints return
/// the canonical <c>PagedResult</c> contract <c>{ items, page, pageSize, totalCount, totalPages }</c> nested under
/// the <c>ApiResponse</c> envelope, i.e. <c>data.items[]</c>. All gated by <c>Payroll.Configure</c>, which the platform admin holds.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class PayrollApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public PayrollApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SalaryComponents_CreateThenList_ReturnsCreatedComponent()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        var createdId = await CreateComponentAsync(client, suffix);
        createdId.Should().NotBeEmpty();

        // Canonical paged contract: data.items[] (PagedResult.Items nested under the ApiResponse envelope).
        var list = await client.GetAsync("/api/v1/payroll/salary-components?page=1&pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var ids = doc.RootElement
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();

        ids.Should().Contain(createdId, "the just-created salary component must appear in the tenant's list");
    }

    [Fact]
    public async Task SalaryStructures_CreateWithLinkedComponentThenList_ReturnsCreatedStructure()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        // A structure links existing components — create one first (the structure's only real FK).
        var componentId = await CreateComponentAsync(client, suffix);

        // CreateSalaryStructureRequest: { name (req, <=100), code (req, ^[A-Za-z0-9_]+$, <=50), description?,
        // effectiveFrom (DateOnly, required != default), isDefault, isActive, components[] }. Created with
        // isActive=false so the "at least one earning before active" (FR-5) service rule does not apply.
        // SalaryStructureComponentInputDto: { salaryComponentId, overrideValue?, overrideFormula?,
        // processingOrder, isMandatory }.
        var create = await client.PostAsJsonAsync("/api/v1/payroll/salary-structures", new
        {
            name = $"Standard Structure {suffix}",
            code = $"STD_{suffix}",
            description = "Standard salary structure (integration test)",
            effectiveFrom = DateTime.UtcNow.Date.ToString("yyyy-MM-dd"),
            isDefault = false,
            isActive = false,
            components = new[]
            {
                new
                {
                    salaryComponentId = componentId,
                    processingOrder = 1,
                    isMandatory = false,
                },
            },
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(create));
        var createdId = await ReadDataIdAsync(create);
        createdId.Should().NotBeEmpty();

        var list = await client.GetAsync("/api/v1/payroll/salary-structures?page=1&pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var ids = doc.RootElement
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();

        ids.Should().Contain(createdId, "the just-created salary structure must appear in the tenant's list");
    }

    /// <summary>
    /// Creates a Fixed-method "Basic" earning component and returns its id.
    /// CreateSalaryComponentRequest: { name (req, <=100), code (req, ^[A-Za-z0-9_]+$, <=50), type (enum:
    /// Earning|Deduction|Statutory|Reimbursement), calculationMethod (enum: Fixed|PercentageOfBasic|
    /// PercentageOfGross|Formula), defaultValue? (required for non-Formula methods), formulaExpression?
    /// (required for Formula), isTaxable (default true), isStatutory, isActive (default true),
    /// processingOrder (>=0) }.
    /// </summary>
    private static async Task<Guid> CreateComponentAsync(HttpClient client, string suffix)
    {
        var create = await client.PostAsJsonAsync("/api/v1/payroll/salary-components", new
        {
            name = $"Basic {suffix}",
            code = $"BASIC_{suffix}",
            type = "Earning",
            calculationMethod = "Fixed",
            defaultValue = 50000m,
            isTaxable = true,
            isStatutory = false,
            isActive = true,
            processingOrder = 1,
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(create));
        return await ReadDataIdAsync(create);
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
