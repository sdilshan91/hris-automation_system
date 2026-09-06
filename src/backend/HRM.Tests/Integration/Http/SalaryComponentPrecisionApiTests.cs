// ============================================================================
// ISSUE-369 (HTTP surface): salary_component.default_value is numeric(18,2), so a
// caller sending 1234.5678 previously got a 201 whose body echoed 1234.5678 while the
// database held 1234.57. Two changes are asserted here, over the real HTTP →
// controller → MediatR → Npgsql path:
//
//   1. The command validators now REJECT a value finer than the column's scale, on
//      both POST and PUT, with a message that names the limit. Silently rounding a
//      money figure and reporting success is the behaviour being removed.
//   2. A create that IS within scale returns exactly what was persisted — asserted
//      against a re-read of the row through a fresh DbContext scope, never against
//      the request literal. Asserting against the literal is precisely the mistake
//      that let the echo ship: the request and a stale echo of it are identical.
//
// NOT IN SCOPE (verified non-reproducible): the finding also claimed 9999999999999999.99
// returns as 1e+16. The whole chain is decimal, the API registers no decimal converter,
// and System.Text.Json never writes a decimal in exponent form. 1e+16 is what a JS or
// jq client prints after coercing the JSON number to an IEEE double — a client-side
// artifact, not an API defect. No custom converter was added.
// ============================================================================

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

[Collection("HttpApi")]
[Trait("TC", "TC-PAY-001")]
public sealed class SalaryComponentPrecisionApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public SalaryComponentPrecisionApiTests(ApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_WithMoreThanTwoDecimalPlaces_Returns400_NamingTheLimit()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var response = await client.PostAsJsonAsync("/api/v1/payroll/salary-components",
            ComponentBody(Suffix(), 1234.5678m));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await BodyAsync(response));
        (await response.Content.ReadAsStringAsync()).Should().Contain("2 decimal places",
            "rounding a money value to fit numeric(18,2) and reporting success hid the loss from the caller");
    }

    [Fact]
    public async Task Update_WithMoreThanTwoDecimalPlaces_Returns400_NamingTheLimit()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        var created = await client.PostAsJsonAsync("/api/v1/payroll/salary-components",
            ComponentBody(suffix, 50000m));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(created));
        var id = await ReadDataIdAsync(created);

        // UpdateAsync carried the identical defect as CreateAsync — the finding under-reported it.
        var response = await client.PutAsJsonAsync($"/api/v1/payroll/salary-components/{id}",
            ComponentBody(suffix, 1234.5678m));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await BodyAsync(response));
        (await response.Content.ReadAsStringAsync()).Should().Contain("2 decimal places");
    }

    [Fact]
    public async Task Create_WithTwoDecimalPlaces_ReturnsExactlyThePersistedValue()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var response = await client.PostAsJsonAsync("/api/v1/payroll/salary-components",
            ComponentBody(Suffix(), 1234.50m));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(response));

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        var id = data.GetProperty("id").GetGuid();
        var responseValue = data.GetProperty("defaultValue").GetDecimal();

        // Read the row back out of the database rather than comparing against the literal that was posted:
        // the request and a stale echo of the request are indistinguishable, which is why the echo shipped.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.SalaryComponents
            .AsNoTracking()
            .IgnoreQueryFilters() // no tenant is resolved in this out-of-band scope
            .Where(c => c.Id == id)
            .Select(c => c.DefaultValue)
            .FirstOrDefaultAsync();

        persisted.Should().NotBeNull();
        responseValue.Should().Be(persisted!.Value,
            "the 201 body is the client's record of what now exists in the database");
    }

    /// <summary>A trailing zero is not over-precision — 1234.50 already fits numeric(18,2) (ignoreTrailingZeros).</summary>
    [Fact]
    public async Task Create_WithATrailingZeroBeyondTwoPlaces_IsAccepted()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var response = await client.PostAsJsonAsync("/api/v1/payroll/salary-components",
            ComponentBody(Suffix(), 1234.5000m));

        response.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(response));
    }

    // ── helpers (mirrors PayrollApiTests) ────────────────────────────────

    private static object ComponentBody(string suffix, decimal defaultValue) => new
    {
        name = $"Basic {suffix}",
        code = $"BASIC_{suffix}",
        type = "Earning",
        calculationMethod = "Fixed",
        defaultValue,
        isTaxable = true,
        isStatutory = false,
        isActive = true,
        processingOrder = 1,
    };

    private static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => $"Response body: {await response.Content.ReadAsStringAsync()}";

    private static async Task<Guid> ReadDataIdAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }
}
