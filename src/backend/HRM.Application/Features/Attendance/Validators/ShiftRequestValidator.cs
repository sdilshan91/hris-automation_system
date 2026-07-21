using System.Globalization;
using FluentValidation;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;

namespace HRM.Application.Features.Attendance.Validators;

/// <summary>
/// Structural validation for a shift create/update request (US-ATT-005 FR-1/FR-2, BR-7/BR-8).
/// Reused by both the create and update command validators. Cross-shift checks (name uniqueness,
/// rotation-step references) are enforced in the service against the DB.
/// </summary>
public sealed class ShiftRequestValidator : AbstractValidator<ShiftRequest>
{
    public ShiftRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Shift name is required.")
            .MaximumLength(100).WithMessage("Shift name cannot exceed 100 characters.");

        RuleFor(x => x.Type)
            .Must(ShiftType.IsValid)
            .WithMessage("Shift type must be one of SINGLE, ROTATING, or FLEXIBLE.");

        RuleFor(x => x.BreakDurationMinutes)
            .InclusiveBetween(0, 1440).WithMessage("Break duration must be between 0 and 1440 minutes.");

        RuleFor(x => x.GracePeriodMinutes)
            .InclusiveBetween(0, 1440).WithMessage("Grace period must be between 0 and 1440 minutes.");

        // DF-56: optional per-shift work-minute overrides. Only validated when supplied (null = unset →
        // falls through to the tenant setting). Range is 0..1440 (one day); 0 is a legitimate value for
        // OvertimeThresholdMinutes ("any excess is overtime").
        // DF-56: floor Standard/Minimum at 1 — a zero standard is nonsensical AND would split the OT record
        // (which treats an explicit 0 as authoritative) from the monthly summary (which re-overrides `<= 0` back
        // to tenant policy, AttendanceSummaryService), breaking the ISSUE-078 reconciliation this DF preserves.
        // (0 remains valid for AutoBreak*/OvertimeThreshold below, where it is a meaningful setting.)
        RuleFor(x => x.StandardWorkMinutes!.Value)
            .InclusiveBetween(1, 1440).WithMessage("Standard work minutes must be between 1 and 1440.")
            .When(x => x.StandardWorkMinutes.HasValue);
        RuleFor(x => x.MinimumWorkMinutes!.Value)
            .InclusiveBetween(1, 1440).WithMessage("Minimum work minutes must be between 1 and 1440.")
            .When(x => x.MinimumWorkMinutes.HasValue);
        RuleFor(x => x.AutoBreakMinutes!.Value)
            .InclusiveBetween(0, 1440).WithMessage("Auto-break minutes must be between 0 and 1440.")
            .When(x => x.AutoBreakMinutes.HasValue);
        RuleFor(x => x.AutoBreakThresholdMinutes!.Value)
            .InclusiveBetween(0, 1440).WithMessage("Auto-break threshold minutes must be between 0 and 1440.")
            .When(x => x.AutoBreakThresholdMinutes.HasValue);
        RuleFor(x => x.OvertimeThresholdMinutes!.Value)
            .InclusiveBetween(0, 1440).WithMessage("Overtime threshold minutes must be between 0 and 1440.")
            .When(x => x.OvertimeThresholdMinutes.HasValue);

        // Time format (HH:mm 24h) when provided.
        RuleFor(x => x.StartTime)
            .Must(BeValidTimeOrNull).WithMessage("Start time must be in HH:mm (24h) format.");
        RuleFor(x => x.EndTime)
            .Must(BeValidTimeOrNull).WithMessage("End time must be in HH:mm (24h) format.");

        // BR-8: FLEXIBLE requires minimum_hours; start/end are optional and not validated for span.
        When(x => x.Type == ShiftType.Flexible, () =>
        {
            RuleFor(x => x.MinimumHours)
                .NotNull().WithMessage("Flexible shifts require minimum hours.")
                .GreaterThan(0).WithMessage("Minimum hours must be greater than zero.")
                .LessThanOrEqualTo(24).WithMessage("Minimum hours cannot exceed 24.");
        });

        // ISSUE-307: EVERY shift type must declare its working days. A Flexible shift used to skip this rule,
        // so it could persist with an empty calendar; wired as a Location/tenant default it then silently
        // deferred to the next resolver tier instead of carrying its own working days. Required for all types
        // now — start/end stay optional for Flexible (BR-8), only the working-day calendar is mandatory.
        RuleFor(x => x.WorkingDays)
            .NotEmpty().WithMessage("At least one working day is required.");

        RuleFor(x => x.WorkingDays)
            .Must(days => days != null && days.All(d => d is >= 1 and <= 7))
            .When(x => x.WorkingDays is { Count: > 0 })
            .WithMessage("Working days must be between 1 (Mon) and 7 (Sun).");

        // SINGLE / ROTATING: start/end required, and BR-7 (start != end).
        When(x => x.Type is ShiftType.Single or ShiftType.Rotating, () =>
        {
            RuleFor(x => x.StartTime)
                .NotEmpty().WithMessage("Start time is required for non-flexible shifts.");
            RuleFor(x => x.EndTime)
                .NotEmpty().WithMessage("End time is required for non-flexible shifts.");

            // BR-7: zero-duration shifts (start == end) are invalid. Night shifts (end < start) ARE
            // allowed (§10) — only equality is rejected.
            RuleFor(x => x)
                .Must(x => x.StartTime != x.EndTime)
                .When(x => BeValidTimeOrNull(x.StartTime) && BeValidTimeOrNull(x.EndTime)
                    && !string.IsNullOrWhiteSpace(x.StartTime) && !string.IsNullOrWhiteSpace(x.EndTime))
                .WithMessage("Start time and end time cannot be the same (zero-duration shift).");
        });

        // ROTATING: rotation pattern required and well-formed.
        When(x => x.Type == ShiftType.Rotating, () =>
        {
            RuleFor(x => x.Rotation)
                .NotNull().WithMessage("Rotating shifts require a rotation pattern.");

            When(x => x.Rotation is not null, () =>
            {
                RuleFor(x => x.Rotation!.CycleLengthDays)
                    .GreaterThan(0).WithMessage("Rotation cycle length must be greater than zero.");

                RuleFor(x => x.Rotation!.Steps)
                    .NotEmpty().WithMessage("A rotation must have at least one step.");

                RuleForEach(x => x.Rotation!.Steps)
                    .Must(s => s.DurationDays > 0)
                    .WithMessage("Each rotation step must cover at least one day.");

                // Sum of step durations must equal the cycle length so every day in the cycle maps.
                RuleFor(x => x.Rotation!)
                    .Must(r => r.Steps.Sum(s => s.DurationDays) == r.CycleLengthDays)
                    .WithMessage("Rotation step durations must sum to the cycle length.");
            });
        });
    }

    private static bool BeValidTimeOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;
        return TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _);
    }
}
