using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration tests for the Performance module (appraisal cycles). Runs as <c>admin@hrm.local</c>
/// on the seeded <c>platform</c> tenant and exercises the genuine HTTP → controller → MediatR → Npgsql path
/// against a throwaway Postgres container.
///
/// <para>Primary happy path: <c>POST/GET /api/v1/tenant/performance/cycles</c>. A cycle is the self-contained
/// Performance primary (goals require a cycle AND an open goal-setting window AND a direct-report employee, so
/// they are not a clean smoke). The create body is the full <c>CreateCycleInput</c> with the three mandatory
/// phases (GoalSetting → SelfAssessment → ManagerReview), each within the cycle window and non-overlapping
/// (CyclePhaseRules). Participant scope <c>AllEmployees</c> resolves to an empty set on a fresh tenant without
/// failing. The list returns <c>ApiResponse&lt;IReadOnlyList&lt;CycleSummaryDto&gt;&gt;</c> → a bare
/// <c>data[]</c> array. Gated by <c>Performance.SetGoal.All</c>, which the platform admin holds.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class PerformanceApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public PerformanceApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Cycles_CreateThenList_ReturnsCreatedCycle()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        // Cycle window: a future 90-day span. The three required phases tile it sequentially.
        var start = DateTime.UtcNow.Date.AddDays(1);
        var end = start.AddDays(90);
        var goalStart = start;
        var goalEnd = start.AddDays(30);
        var selfStart = goalEnd.AddDays(1);
        var selfEnd = selfStart.AddDays(20);
        var reviewStart = selfEnd.AddDays(1);
        var reviewEnd = end;

        static string Iso(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // CreateCycleInput (positional record): Name, Type (enum: Annual|Quarterly|Probation|...),
        // StartDate, EndDate, Phases[] (CyclePhaseInput: PhaseType, StartDate, EndDate),
        // Scope (ParticipantScopeInput: ScopeType (enum: AllEmployees|Departments|Grades|CustomList),
        // DepartmentIds?, EmployeeIds?), RatingScaleMax (2..10), SelfWeightPercent (0..100),
        // Is360Enabled, IsCalibrationEnabled, IsAnonymousFeedback.
        var create = await client.PostAsJsonAsync("/api/v1/tenant/performance/cycles", new
        {
            name = $"Annual Review {suffix}",
            type = "Annual",
            startDate = Iso(start),
            endDate = Iso(end),
            phases = new[]
            {
                new { phaseType = "GoalSetting", startDate = Iso(goalStart), endDate = Iso(goalEnd) },
                new { phaseType = "SelfAssessment", startDate = Iso(selfStart), endDate = Iso(selfEnd) },
                new { phaseType = "ManagerReview", startDate = Iso(reviewStart), endDate = Iso(reviewEnd) },
            },
            // AllEmployees scope needs no department/employee id lists (validator only requires them for
            // Departments / CustomList scopes); the optional members default to null server-side.
            scope = new
            {
                scopeType = "AllEmployees",
            },
            ratingScaleMax = 5,
            selfWeightPercent = 40,
            is360Enabled = false,
            isCalibrationEnabled = false,
            isAnonymousFeedback = false,
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(create));
        var createdId = await ReadDataIdAsync(create);
        createdId.Should().NotBeEmpty();

        // GET /api/v1/tenant/performance/cycles returns ApiResponse<IReadOnlyList<CycleSummaryDto>> → bare data[].
        var list = await client.GetAsync("/api/v1/tenant/performance/cycles");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        var ids = await ReadDataIdsAsync(list);
        ids.Should().Contain(createdId, "the just-created appraisal cycle must appear in the tenant's list");
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
