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

        // SINGLE / ROTATING: working days required, start/end required, and BR-7 (start != end).
        When(x => x.Type is ShiftType.Single or ShiftType.Rotating, () =>
        {
            RuleFor(x => x.WorkingDays)
                .NotEmpty().WithMessage("At least one working day is required.");

            RuleFor(x => x.WorkingDays)
                .Must(days => days.All(d => d is >= 1 and <= 7))
                .WithMessage("Working days must be between 1 (Mon) and 7 (Sun).");

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
