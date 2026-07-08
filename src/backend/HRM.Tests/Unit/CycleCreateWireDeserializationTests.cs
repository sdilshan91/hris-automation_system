using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Enums;
using Xunit;

namespace HRM.Tests.Unit;

/// <summary>
/// JSON-wire regression for <b>BUG-257</b>: the Angular cycle create/update payload used the wrong field
/// names, so the create request either 400'd (its phase types collapsed) or bound scope/weight/360 flags to
/// their <i>defaults</i> — silently corrupting the cycle. Every pre-existing
/// <c>AppraisalCycleServiceTests</c> constructs <see cref="CreateCycleInput"/> / <see cref="CyclePhaseInput"/>
/// / <see cref="ParticipantScopeInput"/> as <b>C# objects</b>, so they bypass JSON deserialization entirely
/// and stayed green through the bug.
///
/// <para>These are the <b>FE-independent</b> arms of the guard: they deserialize the exact JSON shapes with
/// the app's real serializer configuration (<see cref="JsonSerializerDefaults.Web"/> + the global
/// <see cref="JsonStringEnumConverter"/>, mirroring <c>Program.cs AddJsonOptions</c>) and assert:</para>
/// <list type="number">
///   <item>the <b>corrected camelCase payload</b> populates every field (nothing lands on a default), and</item>
///   <item>the <b>old drifted payload</b> silently produces defaults / collapsed phases — locking in that the
///   field NAMES are load-bearing, so any future rename that reintroduces the drift fails here loudly.</item>
/// </list>
/// They do not depend on the running FE, the DB, or the HTTP pipeline, so they must pass now.
/// </summary>
public sealed class CycleCreateWireDeserializationTests
{
    // The app's real controller JSON configuration (Program.cs: AddControllers().AddJsonOptions(...) uses the
    // Web defaults — camelCase, case-insensitive — plus a global JsonStringEnumConverter so enum tokens arrive
    // as their PascalCase names). Deserializing with the SAME options is what makes this a genuine wire test.
    private static readonly JsonSerializerOptions WireOptions = BuildWireOptions();

    private static JsonSerializerOptions BuildWireOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    // A fixed department id the payloads scope to (proves scope.departmentIds round-trips, and that the scope
    // is Departments — NOT the AllEmployees default).
    private const string DepartmentId = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";

    // ── The corrected wire contract the FIXED Angular FE now sends (camelCase) ────────────────────
    private const string CorrectedJson = """
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
      "scope": { "scopeType": "Departments", "departmentIds": ["3f2504e0-4f89-41d3-9a0c-0305e82c3301"], "employeeIds": [] }
    }
    """;

    // ── The OLD drifted payload the buggy FE used to send (the exact field-name drift of BUG-257) ──
    //   selfWeight        (should be selfWeightPercent)
    //   enable360         (should be is360Enabled)
    //   phases[].kind     (should be phases[].phaseType)  ← collapses all phases to the default GoalSetting
    //   scope.type        (should be scope.scopeType)     ← scope collapses to the default AllEmployees
    private const string OldShapeJson = """
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
      "scope": { "type": "Departments", "departmentIds": ["3f2504e0-4f89-41d3-9a0c-0305e82c3301"], "employeeIds": [] }
    }
    """;

    [Fact]
    public void CorrectedWirePayload_Deserializes_AllFieldsPopulated_NoneDefaulted()
    {
        var input = JsonSerializer.Deserialize<CreateCycleInput>(CorrectedJson, WireOptions);

        input.Should().NotBeNull();
        input!.Name.Should().Be("FY2027 Annual Review");
        input.Type.Should().Be(CycleType.Annual);
        input.RatingScaleMax.Should().Be(5);

        // The fields BUG-257 silently corrupted must all bind to their real values, not defaults.
        input.SelfWeightPercent.Should().Be(40, "selfWeightPercent must bind — it defaulted to 0 under the old name");
        input.Is360Enabled.Should().BeTrue("is360Enabled must bind — it defaulted to false under the old name");
        input.IsCalibrationEnabled.Should().BeTrue();

        // The exact BUG-257 failure mode was the three phases collapsing to a single default GoalSetting.
        input.Phases.Should().HaveCount(3);
        input.Phases.Select(p => p.PhaseType).Should().Equal(
            CyclePhaseType.GoalSetting, CyclePhaseType.SelfAssessment, CyclePhaseType.ManagerReview);
        input.Phases.Select(p => p.PhaseType).Distinct().Should().HaveCount(3,
            "the three distinct phase types must survive deserialization (they collapsed to GoalSetting under phases[].kind)");

        // Scope must be Departments with the supplied id — NOT the AllEmployees default (participants scoped).
        input.Scope.Should().NotBeNull();
        input.Scope.ScopeType.Should().Be(ParticipantScopeType.Departments,
            "scopeType must bind to Departments — it defaulted to AllEmployees under the old scope.type name");
        input.Scope.DepartmentIds.Should().ContainSingle().Which.Should().Be(Guid.Parse(DepartmentId));
    }

    [Fact]
    public void OldDriftedWirePayload_Deserializes_ToDefaults_ProvingFieldNamesMatter()
    {
        var input = JsonSerializer.Deserialize<CreateCycleInput>(OldShapeJson, WireOptions);

        input.Should().NotBeNull();

        // Drifted primitive fields silently land on their defaults — the silent-corruption half of BUG-257.
        input!.SelfWeightPercent.Should().Be(0, "'selfWeight' does not bind to SelfWeightPercent — it defaults to 0");
        input.Is360Enabled.Should().BeFalse("'enable360' does not bind to Is360Enabled — it defaults to false");

        // 'phases[].kind' does not bind → every phase's PhaseType defaults to GoalSetting (the 400-causing collapse).
        input.Phases.Should().HaveCount(3);
        input.Phases.Should().OnlyContain(p => p.PhaseType == CyclePhaseType.GoalSetting,
            "phases[].kind does not bind to phaseType, so all three phases collapse to the default GoalSetting");

        // 'scope.type' does not bind → the scope collapses to the AllEmployees default (participants NOT scoped).
        input.Scope.ScopeType.Should().Be(ParticipantScopeType.AllEmployees,
            "scope.type does not bind to scopeType, so the scope silently defaults to AllEmployees");
    }
}
