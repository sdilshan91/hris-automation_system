using FluentValidation;
using HRM.Application.Features.LeaveRequests.Commands;

namespace HRM.Application.Features.LeaveRequests.Validators;

/// <summary>
/// Shape-level validation for a leave-request submission (US-LV-003 FR-1, FR-5, §10).
/// Business rules that require DB access (balance, overlap, holidays, BR-1..BR-6) are
/// enforced in the service layer; this validator only checks the request shape.
/// </summary>
public sealed class CreateLeaveRequestValidator : AbstractValidator<CreateLeaveRequestCommand>
{
    /// <summary>Max attachments per request (§10).</summary>
    public const int MaxAttachments = 3;

    private static readonly string[] s_allowedExtensions = [".pdf", ".jpg", ".jpeg", ".png"];

    public CreateLeaveRequestValidator()
    {
        RuleFor(x => x.LeaveTypeId)
            .NotEmpty().WithMessage("Leave type is required.");

        RuleFor(x => x.StartDate)
            .NotEmpty().WithMessage("Start date is required.");

        RuleFor(x => x.EndDate)
            .NotEmpty().WithMessage("End date is required.")
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("End date must be on or after the start date.");

        // Half-day requests must be a single day with a valid AM/PM session (AC-4).
        When(x => x.IsHalfDay, () =>
        {
            RuleFor(x => x.EndDate)
                .Equal(x => x.StartDate)
                .WithMessage("A half-day leave must start and end on the same day.");

            RuleFor(x => x.HalfDaySession)
                .NotEmpty().WithMessage("Half-day session (AM or PM) is required for a half-day leave.")
                .Must(BeValidSession!)
                .WithMessage("Half-day session must be either 'AM' or 'PM'.");
        });

        // Session must not be supplied for full-day requests.
        When(x => !x.IsHalfDay, () =>
        {
            RuleFor(x => x.HalfDaySession)
                .Empty().WithMessage("Half-day session must not be set for a full-day leave.");
        });

        RuleFor(x => x.Reason)
            .MaximumLength(2000).WithMessage("Reason must not exceed 2000 characters.");

        RuleFor(x => x.Attachments)
            .Must(a => a is null || a.Count <= MaxAttachments)
            .WithMessage($"A maximum of {MaxAttachments} attachments is allowed.");

        RuleForEach(x => x.Attachments)
            .Must(HaveAllowedExtension)
            .WithMessage("Attachments must be PDF, JPG, or PNG files.")
            .When(x => x.Attachments is not null);
    }

    private static bool BeValidSession(string value)
        => string.Equals(value, "AM", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "PM", StringComparison.OrdinalIgnoreCase);

    private static bool HaveAllowedExtension(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Strip any query string before checking the extension.
        var path = url.Split('?', '#')[0];
        var ext = Path.GetExtension(path);
        return s_allowedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }
}
