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

        // ISSUE-036: attachments are now ids of REAL uploaded files. The count cap stays here; type/size are
        // validated at UPLOAD time against the actual bytes (MIME allow-list + magic-byte sniff in
        // LeaveAttachmentService), which is strictly stronger than the extension check this used to do on an
        // unverified client string. Existence/ownership/tenant are resolved in LeaveRequestService.CreateAsync.
        RuleFor(x => x.AttachmentIds)
            .Must(a => a is null || a.Count <= MaxAttachments)
            .WithMessage($"A maximum of {MaxAttachments} attachments is allowed.");

        RuleForEach(x => x.AttachmentIds)
            .NotEmpty().WithMessage("Attachment id must not be empty.")
            .When(x => x.AttachmentIds is not null);
    }

    private static bool BeValidSession(string value)
        => string.Equals(value, "AM", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "PM", StringComparison.OrdinalIgnoreCase);
}
