using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration tests for the Onboarding module (checklist templates). Runs as <c>admin@hrm.local</c>
/// on the seeded <c>platform</c> tenant and exercises the genuine HTTP → controller → MediatR → Npgsql path
/// against a throwaway Postgres container.
///
/// <para>Primary happy path: <c>POST/GET /api/v1/onboarding/templates</c>. A template requires at least one
/// task (BR-2); ApplicableDepartments / ApplicableJobTitles are optional id lists (left empty → a universal
/// template), so no prerequisite entities are needed. The list returns the FE contract
/// <c>{ data, total, page, pageSize }</c> nested under the <c>ApiResponse</c> envelope, i.e. <c>data.data[]</c>.
/// Gated by <c>Onboarding.Manage</c> (write) / <c>Onboarding.View</c> (read), which the platform admin holds.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class OnboardingApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public OnboardingApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Templates_CreateThenList_ReturnsCreatedTemplate()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        // CreateOnboardingTemplateRequest: { templateName (req, 3..200), description?, applicableDepartments[],
        // applicableJobTitles[], isActive (default true), tasks[] (>=1, BR-2) }. Task: { title (req, 3..200),
        // description?, category?, responsibleRole (enum: HR|Manager|IT|Employee|Custom, default HR),
        // responsibleUserId?, dueOffsetDays (>=0), isMandatory, sortOrder (>=0) }.
        var create = await client.PostAsJsonAsync("/api/v1/onboarding/templates", new
        {
            templateName = $"Standard Onboarding {suffix}",
            description = "Standard new-hire onboarding (integration test)",
            isActive = true,
            tasks = new[]
            {
                new
                {
                    title = "Sign employment contract",
                    description = "Collect the signed contract.",
                    category = "Paperwork",
                    responsibleRole = "HR",
                    dueOffsetDays = 0,
                    isMandatory = true,
                    sortOrder = 0,
                },
                new
                {
                    title = "Provision laptop and accounts",
                    description = "Set up hardware and accounts.",
                    category = "IT",
                    responsibleRole = "IT",
                    dueOffsetDays = 1,
                    isMandatory = true,
                    sortOrder = 1,
                },
            },
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(create));
        var createdId = await ReadDataIdAsync(create);
        createdId.Should().NotBeEmpty();

        // Canonical paged contract: data.items[] (PagedResult.Items nested under the ApiResponse envelope).
        var list = await client.GetAsync("/api/v1/onboarding/templates?page=1&pageSize=100");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        using var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var ids = doc.RootElement
            .GetProperty("data")
            .GetProperty("items")
            .EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .ToList();

        ids.Should().Contain(createdId, "the just-created onboarding template must appear in the tenant's list");
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
