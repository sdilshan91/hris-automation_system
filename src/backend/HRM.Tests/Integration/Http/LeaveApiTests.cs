using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration tests for the Leave module. Runs as <c>admin@hrm.local</c> on the seeded
/// <c>platform</c> tenant and exercises the genuine HTTP → controller → MediatR → Npgsql path against a
/// throwaway Postgres container.
///
/// <para>Primary happy path: <c>POST/GET /api/v1/tenant/leave-types</c> (the leave-type configuration the
/// admin owns). The employee leave-request endpoint (<c>POST /api/v1/leaves</c>) requires the acting user to
/// have an Employee row linked by <c>Employee.UserId</c>; the seeded platform admin has none, so that call
/// legitimately returns 403 ("No employee record is linked to the current user.") — this is the documented
/// contract, asserted here rather than worked around.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class LeaveApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public LeaveApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LeaveTypes_CreateThenList_ReturnsCreatedLeaveType()
    {
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        // CreateLeaveTypeRequest: { name (req, <=100), code?, color? (#hex), description?, annualEntitlement (>=0),
        // accrualFrequency ("Monthly|Quarterly|Yearly|Upfront", default "Upfront"), gender ("All|Male|Female",
        // default "All"), + many optional booleans/decimals }. Only Name is structurally required; defaults
        // satisfy AccrualFrequency/Gender.
        var create = await client.PostAsJsonAsync("/api/v1/tenant/leave-types", new
        {
            name = $"Annual Leave {suffix}",
            code = $"AL{suffix}",
            color = "#4CAF50",
            description = "Annual leave (integration test)",
            annualEntitlement = 20m,
            accrualFrequency = "Upfront",
            gender = "All",
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(create));
        var createdId = await ReadDataIdAsync(create);
        createdId.Should().NotBeEmpty();

        // GET /api/v1/tenant/leave-types returns ApiResponse<IReadOnlyList<LeaveTypeDto>> → data is a bare array.
        var list = await client.GetAsync("/api/v1/tenant/leave-types");
        list.StatusCode.Should().Be(HttpStatusCode.OK, await BodyAsync(list));

        var ids = await ReadDataIdsAsync(list);
        ids.Should().Contain(createdId, "the just-created leave type must appear in the tenant's list");
    }

    [Fact]
    public async Task LeaveRequest_AsPlatformAdminWithNoEmployee_IsRejectedWith403()
    {
        // The seeded platform admin has no linked Employee, so the self-service leave-request endpoint
        // must return 403 (LeaveRequestService: "No employee record is linked to the current user."). This is
        // correct, documented behavior — asserted, not hacked. A prerequisite leave type is created first so
        // the request body references a real LeaveTypeId and the 403 is unambiguously the employee-link gate.
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);
        var suffix = Suffix();

        var typeResp = await client.PostAsJsonAsync("/api/v1/tenant/leave-types", new
        {
            name = $"Casual Leave {suffix}",
            code = $"CL{suffix}",
            annualEntitlement = 12m,
        });
        typeResp.StatusCode.Should().Be(HttpStatusCode.Created, await BodyAsync(typeResp));
        var leaveTypeId = await ReadDataIdAsync(typeResp);

        // CreateLeaveRequestRequest: { leaveTypeId, startDate (DateOnly), endDate (DateOnly), isHalfDay,
        // halfDaySession? ("AM"/"PM"), reason?, attachments?, confirmLop }.
        var start = DateTime.UtcNow.Date.AddDays(7);
        var create = await client.PostAsJsonAsync("/api/v1/leaves", new
        {
            leaveTypeId,
            startDate = start.ToString("yyyy-MM-dd"),
            endDate = start.AddDays(1).ToString("yyyy-MM-dd"),
            isHalfDay = false,
            reason = "Integration smoke",
            confirmLop = false,
        });

        create.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the platform admin has no linked Employee, so a self-service leave request is gated. " +
            await BodyAsync(create));
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
