using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HRM.Tests.Integration.Http;

/// <summary>
/// Real HTTP JSON-wire regression for <b>BUG-431</b>: <c>POST /api/v1/tenant/performance/cycles</c> returned
/// <b>500</b> when a date field arrived <b>date-only</b> (<c>"2027-01-01"</c>) instead of UTC-suffixed
/// (<c>"2027-01-01T00:00:00Z"</c>) — the exact shape an Angular <c>&lt;input type="date"&gt;</c> emits.
/// System.Text.Json deserializes an offset-less ISO date to <c>DateTimeKind.Unspecified</c>, and Npgsql then
/// refuses it: <c>ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type
/// 'timestamp with time zone'</c>, which surfaced as an unhandled <c>DbUpdateException</c> → HTTP 500.
///
/// <para>The fix is at the API boundary — a global <c>JsonConverter&lt;DateTime&gt;</c> (+ nullable sibling)
/// registered in <c>Program.cs AddJsonOptions</c> that treats an offset-less value as UTC. This suite is the
/// end-to-end proof: the sibling <c>CycleCreateWirePayloadApiTests</c> only ever posts UTC-suffixed dates, so
/// it stayed green through the bug, and every unit-level cycle test builds <c>CreateCycleInput</c> as a C#
/// object and never touches JSON at all.</para>
///
/// <list type="number">
///   <item><b>Date-only payload</b> (every date field, top-level AND phases, <c>yyyy-MM-dd</c>) → <b>201</b>,
///   and a re-fetch proves the dates persisted as the correct UTC midnight instants (round-trip, not just
///   "didn't throw"). This arm FAILS with a 500 against the unfixed code.</item>
///   <item><b>Malformed date</b> (<c>"not-a-date"</c>) → <b>400</b>, never a 500: schema-invalid input must be
///   rejected at the boundary, which is the other half of the BUG-431 finding.</item>
/// </list>
/// The converter's own unit arms live in <c>HRM.Tests.Unit.UtcDateTimeJsonConverterTests</c>.
/// </summary>
[Collection("HttpApi")]
[Trait("TC", "TC-PRF-004-431")]
public sealed class CycleCreateDateOnlyPayloadApiTests
{
    private const string CyclesRoute = "/api/v1/tenant/performance/cycles";
    private const string PersonaPassword = "Persona@123!";

    private readonly ApiTestFactory _factory;

    public CycleCreateDateOnlyPayloadApiTests(ApiTestFactory factory) => _factory = factory;

    // ── 1. The BUG-431 repro: date-only dates must create the cycle, not 500 ────────────────────────
    [Fact]
    public async Task DateOnlyWirePayload_CreatesCycle_AndPersistsDatesAsUtcMidnight()
    {
        var (subdomain, email) = await SeedTenantWithHrPersonaAsync();
        var client = await _factory.CreateAuthedClientAsync(subdomain, email, PersonaPassword);

        var createResponse = await client.PostAsync(CyclesRoute, JsonBody(DateOnlyJson));
        var createBody = await createResponse.Content.ReadAsStringAsync();

        // BUG-431: this was a 500 ("An unexpected error occurred") because Kind=Unspecified reached Npgsql.
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            $"a date-only payload is the shape the Angular cycle form sends and must create the cycle. Body: {createBody}");

        var createdId = ReadDataGuid(createBody, "id");

        var getResponse = await client.GetAsync($"{CyclesRoute}/{createdId}");
        var getBody = await getResponse.Content.ReadAsStringAsync();
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"re-fetch of the created cycle. Body: {getBody}");

        using var doc = JsonDocument.Parse(getBody);
        var data = doc.RootElement.GetProperty("data");

        // The dates must round-trip as the UTC midnight of the supplied calendar day — proving the boundary
        // coerced the Kind rather than shifting the instant by the server's local offset.
        data.GetProperty("startDate").GetDateTime().ToUniversalTime()
            .Should().Be(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                "the date-only startDate must persist as UTC midnight of that calendar day");
        data.GetProperty("endDate").GetDateTime().ToUniversalTime()
            .Should().Be(new DateTime(2027, 4, 30, 0, 0, 0, DateTimeKind.Utc));

        var phases = data.GetProperty("phases").EnumerateArray()
            .ToDictionary(p => p.GetProperty("phaseType").GetString()!, p => p);
        phases.Keys.Should().BeEquivalentTo(new[] { "GoalSetting", "SelfAssessment", "ManagerReview" });
        phases["GoalSetting"].GetProperty("startDate").GetDateTime().ToUniversalTime()
            .Should().Be(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                "phase dates arrive date-only too and must survive the same way");
        phases["ManagerReview"].GetProperty("endDate").GetDateTime().ToUniversalTime()
            .Should().Be(new DateTime(2027, 2, 15, 0, 0, 0, DateTimeKind.Utc));
    }

    // ── 2. A genuinely malformed date must be a 400 at the boundary, never a 500 ───────────────────
    [Fact]
    public async Task MalformedDateWirePayload_Returns400_NotServerError()
    {
        var (subdomain, email) = await SeedTenantWithHrPersonaAsync();
        var client = await _factory.CreateAuthedClientAsync(subdomain, email, PersonaPassword);

        var response = await client.PostAsync(CyclesRoute, JsonBody(MalformedDateJson));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"an unparseable date is schema-invalid input and must be rejected at the boundary. Body: {body}");
        body.Should().NotContain("An unexpected error occurred",
            "a malformed date must never reach the unhandled-exception path");
    }

    // ── payloads ───────────────────────────────────────────────────────────────────────────────────

    // Every date field date-only (yyyy-MM-dd) — arm A of the BUG-431 evidence table, i.e. exactly what
    // Angular's <input type="date"> yields when passed through unconverted.
    private const string DateOnlyJson = """
    {
      "name": "BUG-431 Date-Only Cycle",
      "type": "Annual",
      "startDate": "2027-01-01",
      "endDate": "2027-04-30",
      "ratingScaleMax": 5,
      "selfWeightPercent": 40,
      "is360Enabled": false,
      "isCalibrationEnabled": false,
      "isAnonymousFeedback": false,
      "phases": [
        { "phaseType": "GoalSetting",    "startDate": "2027-01-01", "endDate": "2027-01-15" },
        { "phaseType": "SelfAssessment", "startDate": "2027-01-16", "endDate": "2027-01-31" },
        { "phaseType": "ManagerReview",  "startDate": "2027-02-01", "endDate": "2027-02-15" }
      ],
      "scope": { "scopeType": "AllEmployees", "departmentIds": [], "employeeIds": [] }
    }
    """;

    // Same payload with one unparseable date.
    private const string MalformedDateJson = """
    {
      "name": "BUG-431 Malformed Date Cycle",
      "type": "Annual",
      "startDate": "not-a-date",
      "endDate": "2027-04-30",
      "ratingScaleMax": 5,
      "selfWeightPercent": 40,
      "is360Enabled": false,
      "isCalibrationEnabled": false,
      "isAnonymousFeedback": false,
      "phases": [
        { "phaseType": "GoalSetting",    "startDate": "2027-01-01", "endDate": "2027-01-15" },
        { "phaseType": "SelfAssessment", "startDate": "2027-01-16", "endDate": "2027-01-31" },
        { "phaseType": "ManagerReview",  "startDate": "2027-02-01", "endDate": "2027-02-15" }
      ],
      "scope": { "scopeType": "AllEmployees", "departmentIds": [], "employeeIds": [] }
    }
    """;

    private static StringContent JsonBody(string json) =>
        new(json, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json").MediaType!);

    private static Guid ReadDataGuid(string responseBody, string property)
    {
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("data").GetProperty(property).GetGuid();
    }

    // ── seeding ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a fresh Active business tenant with an HR persona (holding <c>Performance.SetGoal.All</c> so it can
    /// create cycles) and one active employee, so the AllEmployees scope resolves a real participant set.
    /// </summary>
    private async Task<(string Subdomain, string Email)> SeedTenantWithHrPersonaAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenantId = Guid.NewGuid();
        var subdomain = $"b431{Guid.NewGuid():N}"[..14];
        var departmentId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Subdomain = subdomain,
            Name = "BUG-431 Date Only",
            Status = TenantStatus.Active,
            PlanId = "default",
        });

        // Department + job title first — the employees FKs are enforced on real Postgres.
        db.Departments.Add(new Department
        {
            Id = departmentId, TenantId = tenantId, Name = "BUG-431 Dept", Code = "B431-D", IsActive = true,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = "BUG-431 Engineer", IsActive = true,
        });

        // TenantId is set explicitly: the SaveChanges tenant interceptor does not stamp in this seeding scope.
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = "B431-1", FirstName = "Grace", LastName = "H",
            Email = "grace@bug431.test", Status = EmployeeStatus.Active, DepartmentId = departmentId,
            JobTitleId = jobTitleId, EmploymentType = EmploymentType.FullTime,
        });

        var role = new Role
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = PermissionCatalog.BuiltInRoles.HROfficer,
            IsBuiltIn = true,
            RolePermissions = PermissionCatalog.DefaultPermissionsFor(PermissionCatalog.BuiltInRoles.HROfficer)
                .Select(p => new RolePermission { Permission = p })
                .ToList(),
        };
        db.Roles.Add(role);

        var email = $"hr-{Guid.NewGuid():N}@bug431.test";
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            IsActive = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(PersonaPassword, workFactor: 12),
        };
        db.Users.Add(user);

        var membership = new UserTenant
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TenantId = tenantId,
            Status = UserTenantStatus.Active,
        };
        db.UserTenants.Add(membership);
        db.UserTenantRoles.Add(new UserTenantRole
        {
            UserTenantId = membership.Id,
            RoleId = role.Id,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = "test",
        });

        await db.SaveChangesAsync();
        return (subdomain, email);
    }
}
