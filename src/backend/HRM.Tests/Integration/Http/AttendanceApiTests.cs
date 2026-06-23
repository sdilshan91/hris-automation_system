using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP integration tests for the Attendance module. Runs as <c>admin@hrm.local</c> on the seeded
/// <c>platform</c> tenant and exercises the genuine HTTP → controller → MediatR → Npgsql path against a
/// throwaway Postgres container.
///
/// <para>The self-service clock endpoints resolve the acting Employee via <c>Employee.UserId ==
/// ICurrentUser.UserId</c> (AttendanceService). The seeded platform admin has no Employee row, so both the
/// write path <c>POST /api/v1/attendance/clock-in</c> AND the read path <c>GET /api/v1/attendance/status</c>
/// legitimately return 403 ("No employee record is linked to the current user.") — the same employee-link
/// gate, applied uniformly (AttendanceService.GetClockStatusAsync fails closed when no Employee is linked).
/// Both are the documented contract, asserted here rather than worked around.</para>
/// </summary>
[Collection("HttpApi")]
public sealed class AttendanceApiTests
{
    private const string Subdomain = "platform";
    private const string AdminEmail = "admin@hrm.local";
    private const string AdminPassword = "Admin@123!";

    private readonly ApiTestFactory _factory;

    public AttendanceApiTests(ApiTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ClockStatus_AsPlatformAdminWithNoEmployee_IsRejectedWith403()
    {
        // GET /api/v1/attendance/status resolves the acting Employee by Employee.UserId == current user and
        // fails closed with 403 when none is linked (AttendanceService.GetClockStatusAsync: "No employee
        // record is linked to the current user."). The seeded platform admin has no Employee row, so the
        // read path is gated identically to clock-in below — the documented contract, asserted not hacked.
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var status = await client.GetAsync("/api/v1/attendance/status");

        status.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the platform admin has no linked Employee, so the clock-status read is gated. " +
            await BodyAsync(status));
    }

    [Fact]
    public async Task ClockIn_AsPlatformAdminWithNoEmployee_IsRejectedWith403()
    {
        // ClockInRequest: { latitude?, longitude?, photoUrl?, source? }. All fields optional; the tenant's
        // default geo/photo policy is permissive, so the request is structurally valid and reaches the
        // employee-link gate, which returns 403 for the unlinked platform admin (correct, documented behavior).
        var client = await _factory.CreateAuthedClientAsync(Subdomain, AdminEmail, AdminPassword);

        var clockIn = await client.PostAsJsonAsync("/api/v1/attendance/clock-in", new
        {
            source = "WEB",
        });

        clockIn.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the platform admin has no linked Employee, so self clock-in is gated. " +
            await BodyAsync(clockIn));
    }

    // ── helpers (mirrors CoreHrApiTests) ─────────────────────────────────

    private static async Task<string> BodyAsync(HttpResponseMessage response)
        => $"Response body: {await response.Content.ReadAsStringAsync()}";
}
