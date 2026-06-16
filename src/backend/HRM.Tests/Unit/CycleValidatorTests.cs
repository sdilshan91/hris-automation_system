// ============================================================================
// US-PRF-004: cycle command validators — unit tests.
// Covers the stateless phase-set rules verifiable from the command fields alone
// (FR-1/FR-2/BR-3), which live in CyclePhaseRules and are shared by Create/Update:
//   - FR-1: the three core phases (goal-setting, self-assessment, manager-review) are required.
//   - FR-2: phases must be sequential + non-overlapping (overlapping rejected).
//   - FR-2: each phase start < end (reversed rejected).
//   - BR-3: every phase within the cycle's overall window (out-of-range rejected).
//   - cycle start < end; no duplicate phase types.
//   - scope-specific requirements (department list / custom list non-empty).
//   - BR-6: cancel transition requires a reason.
// The stateful rules (BR-4/BR-5/BR-2/status machine/participant resolution) are DB-driven
// and exercised by AppraisalCycleServiceTests + AppraisalCycleIntegrationTests, not here.
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.Performance.Commands;
using HRM.Application.Features.Performance.DTOs;
using HRM.Application.Features.Performance.Validators;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class CycleValidatorTests
{
    private readonly CreateCycleCommandValidator _create = new();
    private readonly UpdateCycleCommandValidator _update = new();
    private readonly CloneCycleCommandValidator _clone = new();
    private readonly TransitionCycleStatusCommandValidator _transition = new();

    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddDays(60);

    private static IReadOnlyList<CyclePhaseInput> ValidPhases() =>
    [
        new(CyclePhaseType.GoalSetting, Start, Start.AddDays(10)),
        new(CyclePhaseType.SelfAssessment, Start.AddDays(11), Start.AddDays(20)),
        new(CyclePhaseType.ManagerReview, Start.AddDays(21), Start.AddDays(30)),
    ];

    private static CreateCycleCommand CreateCmd(
        IReadOnlyList<CyclePhaseInput>? phases = null,
        DateTime? start = null,
        DateTime? end = null,
        ParticipantScopeInput? scope = null,
        int ratingScaleMax = 5,
        int selfWeightPercent = 30)
        => new(new CreateCycleInput(
            "FY2026 Annual", CycleType.Annual, start ?? Start, end ?? End,
            phases ?? ValidPhases(), scope ?? new ParticipantScopeInput(ParticipantScopeType.AllEmployees),
            ratingScaleMax, selfWeightPercent,
            Is360Enabled: false, IsCalibrationEnabled: false, IsAnonymousFeedback: false));

    // ── Happy path ───────────────────────────────────────────────────────

    [Fact]
    public void Create_HappyPath_IsValid()
    {
        _create.TestValidate(CreateCmd()).ShouldNotHaveAnyValidationErrors();
    }

    // ── FR-1: the three core phases required ─────────────────────────────

    [Fact]
    public void Create_MissingManagerReviewPhase_HasError()
    {
        IReadOnlyList<CyclePhaseInput> phases =
        [
            new(CyclePhaseType.GoalSetting, Start, Start.AddDays(10)),
            new(CyclePhaseType.SelfAssessment, Start.AddDays(11), Start.AddDays(20)),
        ];
        _create.TestValidate(CreateCmd(phases)).ShouldHaveValidationErrorFor("Phases");
    }

    [Fact]
    public void Create_NoPhases_HasError()
    {
        _create.TestValidate(CreateCmd(Array.Empty<CyclePhaseInput>())).ShouldHaveValidationErrorFor("Phases");
    }

    // ── FR-2: reversed phase (start >= end) ──────────────────────────────

    [Fact]
    public void Create_ReversedPhaseDates_HasError()
    {
        IReadOnlyList<CyclePhaseInput> phases =
        [
            new(CyclePhaseType.GoalSetting, Start.AddDays(10), Start), // reversed
            new(CyclePhaseType.SelfAssessment, Start.AddDays(11), Start.AddDays(20)),
            new(CyclePhaseType.ManagerReview, Start.AddDays(21), Start.AddDays(30)),
        ];
        _create.TestValidate(CreateCmd(phases)).ShouldHaveValidationErrorFor("Phases");
    }

    // ── FR-2: overlapping phases ─────────────────────────────────────────

    [Fact]
    public void Create_OverlappingPhases_HasError()
    {
        IReadOnlyList<CyclePhaseInput> phases =
        [
            new(CyclePhaseType.GoalSetting, Start, Start.AddDays(15)),
            new(CyclePhaseType.SelfAssessment, Start.AddDays(10), Start.AddDays(20)), // overlaps goal-setting
            new(CyclePhaseType.ManagerReview, Start.AddDays(21), Start.AddDays(30)),
        ];
        _create.TestValidate(CreateCmd(phases)).ShouldHaveValidationErrorFor("Phases");
    }

    // ── BR-3: phase out of cycle range ───────────────────────────────────

    [Fact]
    public void Create_PhaseBeforeCycleStart_HasError()
    {
        IReadOnlyList<CyclePhaseInput> phases =
        [
            new(CyclePhaseType.GoalSetting, Start.AddDays(-5), Start.AddDays(10)), // starts before cycle
            new(CyclePhaseType.SelfAssessment, Start.AddDays(11), Start.AddDays(20)),
            new(CyclePhaseType.ManagerReview, Start.AddDays(21), Start.AddDays(30)),
        ];
        _create.TestValidate(CreateCmd(phases)).ShouldHaveValidationErrorFor("Phases");
    }

    [Fact]
    public void Create_PhaseAfterCycleEnd_HasError()
    {
        IReadOnlyList<CyclePhaseInput> phases =
        [
            new(CyclePhaseType.GoalSetting, Start, Start.AddDays(10)),
            new(CyclePhaseType.SelfAssessment, Start.AddDays(11), Start.AddDays(20)),
            new(CyclePhaseType.ManagerReview, Start.AddDays(21), End.AddDays(10)), // ends after cycle
        ];
        _create.TestValidate(CreateCmd(phases)).ShouldHaveValidationErrorFor("Phases");
    }

    // ── cycle start >= end ───────────────────────────────────────────────

    [Fact]
    public void Create_CycleStartAfterEnd_HasError()
    {
        _create.TestValidate(CreateCmd(start: End, end: Start)).ShouldHaveValidationErrorFor("StartDate");
    }

    // ── no duplicate phase types ─────────────────────────────────────────

    [Fact]
    public void Create_DuplicatePhaseType_HasError()
    {
        IReadOnlyList<CyclePhaseInput> phases =
        [
            new(CyclePhaseType.GoalSetting, Start, Start.AddDays(10)),
            new(CyclePhaseType.GoalSetting, Start.AddDays(11), Start.AddDays(20)), // duplicate
            new(CyclePhaseType.SelfAssessment, Start.AddDays(21), Start.AddDays(25)),
            new(CyclePhaseType.ManagerReview, Start.AddDays(26), Start.AddDays(30)),
        ];
        _create.TestValidate(CreateCmd(phases)).ShouldHaveValidationErrorFor("Phases");
    }

    // ── scope requirements ───────────────────────────────────────────────

    [Fact]
    public void Create_DepartmentScope_WithNoDepartments_HasError()
    {
        var scope = new ParticipantScopeInput(ParticipantScopeType.Departments, DepartmentIds: Array.Empty<Guid>());
        _create.TestValidate(CreateCmd(scope: scope)).ShouldHaveValidationErrorFor(x => x.Input.Scope);
    }

    [Fact]
    public void Create_CustomListScope_WithNoEmployees_HasError()
    {
        var scope = new ParticipantScopeInput(ParticipantScopeType.CustomList, EmployeeIds: Array.Empty<Guid>());
        _create.TestValidate(CreateCmd(scope: scope)).ShouldHaveValidationErrorFor(x => x.Input.Scope);
    }

    [Fact]
    public void Create_EmptyName_HasError()
    {
        var cmd = new CreateCycleCommand(CreateCmd().Input with { Name = "" });
        _create.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Input.Name);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public void Create_RatingScaleOutOfBounds_HasError(int scale)
    {
        _create.TestValidate(CreateCmd(ratingScaleMax: scale)).ShouldHaveValidationErrorFor(x => x.Input.RatingScaleMax);
    }

    // ── Update validator shares the same phase rules ─────────────────────

    [Fact]
    public void Update_HappyPath_IsValid()
    {
        var cmd = new UpdateCycleCommand(Guid.NewGuid(), new UpdateCycleInput(
            "FY2026 Annual", Start, End, ValidPhases(),
            Is360Enabled: false, IsCalibrationEnabled: false, IsAnonymousFeedback: false,
            RatingScaleMax: 5, SelfWeightPercent: 30));
        _update.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Update_OverlappingPhases_HasError()
    {
        IReadOnlyList<CyclePhaseInput> phases =
        [
            new(CyclePhaseType.GoalSetting, Start, Start.AddDays(15)),
            new(CyclePhaseType.SelfAssessment, Start.AddDays(10), Start.AddDays(20)),
            new(CyclePhaseType.ManagerReview, Start.AddDays(21), Start.AddDays(30)),
        ];
        var cmd = new UpdateCycleCommand(Guid.NewGuid(), new UpdateCycleInput(
            "FY2026 Annual", Start, End, phases,
            Is360Enabled: false, IsCalibrationEnabled: false, IsAnonymousFeedback: false,
            RatingScaleMax: null, SelfWeightPercent: null));
        _update.TestValidate(cmd).ShouldHaveValidationErrorFor("Phases");
    }

    // ── Clone validator ──────────────────────────────────────────────────

    [Fact]
    public void Clone_HappyPath_IsValid()
    {
        var cmd = new CloneCycleCommand(new CloneCycleInput(Guid.NewGuid(), "Q2 KPI", Start, End));
        _clone.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Clone_StartAfterEnd_HasError()
    {
        var cmd = new CloneCycleCommand(new CloneCycleInput(Guid.NewGuid(), "Q2 KPI", End, Start));
        _clone.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Input);
    }

    // ── Transition validator (BR-6) ──────────────────────────────────────

    [Fact]
    public void Transition_Cancel_WithoutReason_HasError()
    {
        var cmd = new TransitionCycleStatusCommand(Guid.NewGuid(), AppraisalCycleStatus.Cancelled, null);
        _transition.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Reason);
    }

    [Fact]
    public void Transition_Cancel_WithReason_IsValid()
    {
        var cmd = new TransitionCycleStatusCommand(Guid.NewGuid(), AppraisalCycleStatus.Cancelled, "Budget cut.");
        _transition.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Transition_Activate_WithoutReason_IsValid()
    {
        var cmd = new TransitionCycleStatusCommand(Guid.NewGuid(), AppraisalCycleStatus.Active, null);
        _transition.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }
}
