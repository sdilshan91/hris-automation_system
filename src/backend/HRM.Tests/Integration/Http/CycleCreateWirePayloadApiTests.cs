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
/// Real HTTP JSON-wire regression for <b>BUG-257</b>: the Angular cycle-create payload used the wrong field
/// names, so <c>POST /api/v1/tenant/performance/cycles</c> either 400'd (its phase types collapsed to a single
/// default <c>GoalSetting</c>) or bound scope/weight/360 flags to their defaults — silently corrupting the
/// cycle. The existing <c>AppraisalCycleServiceTests</c> build <c>CreateCycleInput</c> as C# objects and so
/// never exercise deserialization; they stayed green through the bug.
///
/// <para>These tests POST the <b>raw JSON string</b> (as <c>application/json</c> <see cref="StringContent"/>,
/// NOT a typed object) through the genuine HTTP → model-binding → validation → MediatR → Npgsql path as an HR
/// persona, so a name-drift regression fails loudly here:</para>
/// <list type="number">
///   <item><b>Corrected camelCase payload</b> → create succeeds (201, not 400 — the phases did NOT collapse),
///   and a re-fetch proves the fields round-tripped: <c>selfWeightPercent=40</c>, <c>is360Enabled=true</c>,
///   <c>isCalibrationEnabled=true</c>, three distinct persisted phase types, and scope <c>Departments</c>
///   (participants scoped to the one target department — NOT the AllEmployees default).</item>
///   <item><b>Old drifted payload</b> (<c>selfWeight</c> / <c>enable360</c> / <c>phases[].kind</c> /
///   <c>scope.type</c>) → the phases collapse to a single default GoalSetting and the create is rejected
///   (400), documenting the exact failure mode and locking in that the field names matter.</item>
/// </list>
/// The FE-independent <c>System.Text.Json</c> deserialization arms live in
/// <c>HRM.Tests.Unit.CycleCreateWireDeserializationTests</c>.
/// </summary>
[Collection("HttpApi")]
public sealed class CycleCreateWirePayloadApiTests
{
    private const string CyclesRoute = "/api/v1/tenant/performance/cycles";
    private const string PersonaPassword = "Persona@123!";

    private readonly ApiTestFactory _factory;

    public CycleCreateWirePayloadApiTests(ApiTestFactory factory) => _factory = factory;

    // ── 1. POSITIVE: the corrected FE payload creates the cycle AND round-trips every field ─────────
    [Fact]
    public async Task CorrectedWirePayload_CreatesCycle_AndRoundTripsAllFields()
    {
        var (subdomain, email, targetDeptId, _) = await SeedTenantWithHrPersonaAndTwoDeptsAsync();
        var client = await _factory.CreateAuthedClientAsync(subdomain, email, PersonaPassword);

        var createResponse = await client.PostAsync(CyclesRoute, JsonBody(CorrectedJson(targetDeptId)));
        var createBody = await createResponse.Content.ReadAsStringAsync();

        // The exact BUG-257 failure mode was the phase types collapsing to a single GoalSetting, which the
        // validator rejects with a 400. A genuine 201 proves the corrected phaseType field bound for all three.
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            $"the corrected camelCase payload must create the cycle, not 400 on collapsed phases. Body: {createBody}");

        var createdId = ReadDataGuid(createBody, "id");

        // Re-fetch the persisted cycle and assert the fields the bug corrupted survived the wire.
        var getResponse = await client.GetAsync($"{CyclesRoute}/{createdId}");
        var getBody = await getResponse.Content.ReadAsStringAsync();
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"re-fetch of the created cycle. Body: {getBody}");

        using var doc = JsonDocument.Parse(getBody);
        var data = doc.RootElement.GetProperty("data");

        data.GetProperty("ratingScaleMax").GetInt32().Should().Be(5);
        data.GetProperty("selfWeightPercent").GetInt32().Should().Be(40,
            "selfWeightPercent must round-trip as 40, not default to 0");
        data.GetProperty("is360Enabled").GetBoolean().Should().BeTrue(
            "is360Enabled must round-trip as true, not default to false");
        data.GetProperty("isCalibrationEnabled").GetBoolean().Should().BeTrue();

        // The three distinct phase types must persist (they collapsed to one GoalSetting under the old shape).
        var persistedPhaseTypes = data.GetProperty("phases").EnumerateArray()
            .Select(p => p.GetProperty("phaseType").GetString())
            .ToList();
        persistedPhaseTypes.Should().BeEquivalentTo(
            new[] { "GoalSetting", "SelfAssessment", "ManagerReview" },
            "all three distinct phases must persist — the bug collapsed them to a single GoalSetting");

        // Scope must be Departments (serialized as its enum name), NOT the AllEmployees default. And because the
        // scope resolved to the ONE target department (which holds exactly one active employee, while a second
        // active employee sits in a different department), the participant count is 1 — behaviourally proving the
        // participants were scoped to the department, not defaulted to all employees.
        data.GetProperty("participantScope").GetString().Should().Be("Departments",
            "the scope must round-trip as Departments, not default to AllEmployees");
        data.GetProperty("participantCount").GetInt32().Should().Be(1,
            "participants must be scoped to the one target department (1 active employee), not all employees (2)");
    }

    // ── 2. NEGATIVE: the OLD drifted payload collapses the phases and is rejected ───────────────────
    [Fact]
    public async Task OldDriftedWirePayload_CollapsesPhases_AndIsRejected()
    {
        var (subdomain, email, targetDeptId, _) = await SeedTenantWithHrPersonaAndTwoDeptsAsync();
        var client = await _factory.CreateAuthedClientAsync(subdomain, email, PersonaPassword);

        var response = await client.PostAsync(CyclesRoute, JsonBody(OldShapeJson(targetDeptId)));
        var body = await response.Content.ReadAsStringAsync();

        // 'phases[].kind' does not bind → every phase's PhaseType defaults to GoalSetting → the validator rejects
        // it (duplicate phase types / missing SelfAssessment + ManagerReview). This 400 is the BUG-257 failure the
        // corrected contract fixes, and locks in that the field names are load-bearing.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"the old drifted payload must be rejected — phases[].kind collapses all phases to GoalSetting. Body: {body}");
    }

    // ── payloads ────────────────────────────────────────────────────────────────────────────────────

    // The corrected wire contract the FIXED Angular FE now sends (camelCase). managerWeightPercent is
    // intentionally absent — CreateCycleInput has no such field (the manager weight is derived, not supplied).
    private static string CorrectedJson(Guid targetDeptId) => $$"""
    {
      "name": "FY2027 Annual Review",
      "type": "Annual",
      "startDate": "2027-01-01T00:00:00Z",
      "endDate": "2027-04-30T00:00:00Z",
      "ratingScaleMax": 5,
      "selfWeightPercent": 40,
      "is360Enabled": true,
      "isCalibrationEnabled": true,
      "isAnonymousFeedback": false,
      "phases": [
        { "phaseType": "GoalSetting",    "startDate": "2027-01-01T00:00:00Z", "endDate": "2027-01-15T00:00:00Z" },
        { "phaseType": "SelfAssessment", "startDate": "2027-01-16T00:00:00Z", "endDate": "2027-01-31T00:00:00Z" },
        { "phaseType": "ManagerReview",  "startDate": "2027-02-01T00:00:00Z", "endDate": "2027-02-15T00:00:00Z" }
      ],
      "scope": { "scopeType": "Departments", "departmentIds": ["{{targetDeptId}}"], "employeeIds": [] }
    }
    """;

    // The OLD drifted payload the buggy FE sent: selfWeight / enable360 / phases[].kind / scope.type.
    private static string OldShapeJson(Guid targetDeptId) => $$"""
    {
      "name": "FY2027 Annual Review",
      "type": "Annual",
      "startDate": "2027-01-01T00:00:00Z",
      "endDate": "2027-04-30T00:00:00Z",
      "ratingScaleMax": 5,
      "selfWeight": 40,
      "enable360": true,
      "isCalibrationEnabled": true,
      "phases": [
        { "kind": "GoalSetting",    "startDate": "2027-01-01T00:00:00Z", "endDate": "2027-01-15T00:00:00Z" },
        { "kind": "SelfAssessment", "startDate": "2027-01-16T00:00:00Z", "endDate": "2027-01-31T00:00:00Z" },
        { "kind": "ManagerReview",  "startDate": "2027-02-01T00:00:00Z", "endDate": "2027-02-15T00:00:00Z" }
      ],
      "scope": { "type": "Departments", "departmentIds": ["{{targetDeptId}}"], "employeeIds": [] }
    }
    """;

    private static StringContent JsonBody(string json) =>
        new(json, Encoding.UTF8, MediaTypeHeaderValue.Parse("application/json").MediaType!);

    private static Guid ReadDataGuid(string responseBody, string property)
    {
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("data").GetProperty(property).GetGuid();
    }

    // ── seeding ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a fresh Active business tenant with an HR persona (holding <c>Performance.SetGoal.All</c> so it can
    /// create cycles) and two active employees in two different departments. Returns the tenant subdomain, the
    /// HR login email, the TARGET department id (holds exactly one active employee) and the other department id.
    /// Because the target department holds one of the two active employees, a Departments-scoped create resolves
    /// exactly one participant — distinguishing it from the AllEmployees default (which would resolve two).
    /// </summary>
    private async Task<(string Subdomain, string Email, Guid TargetDeptId, Guid OtherDeptId)>
        SeedTenantWithHrPersonaAndTwoDeptsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var tenantId = Guid.NewGuid();
        var subdomain = $"b257{Guid.NewGuid():N}"[..14];
        var targetDeptId = Guid.NewGuid();
        var otherDeptId = Guid.NewGuid();
        var jobTitleId = Guid.NewGuid();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Subdomain = subdomain,
            Name = "BUG-257 Cycle Wire",
            Status = TenantStatus.Active,
            PlanId = "default",
        });

        // The two departments must exist first — the employees.department_id FK is enforced on real Postgres.
        db.Departments.Add(new Department
        {
            Id = targetDeptId, TenantId = tenantId, Name = "BUG-257 Target Dept", Code = "B257-TGT", IsActive = true,
        });
        db.Departments.Add(new Department
        {
            Id = otherDeptId, TenantId = tenantId, Name = "BUG-257 Other Dept", Code = "B257-OTH", IsActive = true,
        });

        // A job title too — the employees.job_title_id FK is enforced on real Postgres.
        db.JobTitles.Add(new JobTitle
        {
            Id = jobTitleId, TenantId = tenantId, TitleName = "BUG-257 Engineer", IsActive = true,
        });

        // Two active employees in two departments. TenantId is set explicitly because the SaveChanges tenant
        // interceptor does not stamp when no tenant is resolved in this seeding scope.
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = "B257-1", FirstName = "Ada", LastName = "L",
            Email = "ada@bug257.test", Status = EmployeeStatus.Active, DepartmentId = targetDeptId,
            JobTitleId = jobTitleId, EmploymentType = EmploymentType.FullTime,
        });
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(), TenantId = tenantId, EmployeeNo = "B257-2", FirstName = "Alan", LastName = "T",
            Email = "alan@bug257.test", Status = EmployeeStatus.Active, DepartmentId = otherDeptId,
            JobTitleId = jobTitleId, EmploymentType = EmploymentType.FullTime,
        });

        // An HR role carrying the real built-in permission set (includes Performance.SetGoal.All → can create).
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

        var email = $"hr-{Guid.NewGuid():N}@bug257.test";
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
        return (subdomain, email, targetDeptId, otherDeptId);
    }
}
