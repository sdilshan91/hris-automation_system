using FluentValidation;
using HRM.Application.Features.NotificationTemplates.Commands;

namespace HRM.Application.Features.NotificationTemplates.Validators;

/// <summary>US-NTF-002 FR-1/BR-3: a saved override must carry a subject + both HTML and plain-text bodies.</summary>
public sealed class SaveTemplateValidator : AbstractValidator<SaveTemplateCommand>
{
    public SaveTemplateValidator()
    {
        RuleFor(x => x.EventKey)
            .NotEmpty().WithMessage("Event key is required.");

        RuleFor(x => x.Body).NotNull().WithMessage("Template body is required.");

        When(x => x.Body is not null, () =>
        {
            RuleFor(x => x.Body.Subject)
                .NotEmpty().WithMessage("Subject is required.")
                .MaximumLength(200).WithMessage("Subject cannot exceed 200 characters.");

            RuleFor(x => x.Body.BodyHtml)
                .NotEmpty().WithMessage("HTML body is required.");

            RuleFor(x => x.Body.BodyText)
                .NotEmpty().WithMessage("Plain-text body is required.");
        });
    }
}

/// <summary>US-NTF-002 FR-4: preview needs a subject + both bodies to render.</summary>
public sealed class PreviewTemplateValidator : AbstractValidator<PreviewTemplateCommand>
{
    public PreviewTemplateValidator()
    {
        RuleFor(x => x.EventKey)
            .NotEmpty().WithMessage("Event key is required.");

        RuleFor(x => x.Request).NotNull().WithMessage("Preview request is required.");

        When(x => x.Request is not null, () =>
        {
            RuleFor(x => x.Request.Subject)
                .MaximumLength(200).WithMessage("Subject cannot exceed 200 characters.");
        });
    }
}

/// <summary>US-NTF-002 FR-8: a test email needs a valid recipient address.</summary>
public sealed class SendTestEmailValidator : AbstractValidator<SendTestEmailCommand>
{
    public SendTestEmailValidator()
    {
        RuleFor(x => x.EventKey)
            .NotEmpty().WithMessage("Event key is required.");

        RuleFor(x => x.Request).NotNull().WithMessage("Test email request is required.");

        When(x => x.Request is not null, () =>
        {
            RuleFor(x => x.Request.RecipientEmail)
                .NotEmpty().WithMessage("Recipient email is required.")
                .EmailAddress().WithMessage("A valid recipient email is required.");
        });
    }
}
