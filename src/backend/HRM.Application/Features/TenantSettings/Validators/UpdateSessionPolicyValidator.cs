using FluentValidation;
using HRM.Application.Features.TenantSettings.Commands;

namespace HRM.Application.Features.TenantSettings.Validators;

/// <summary>US-ADM-006 FR-6: validate the session-policy update.</summary>
public sealed class UpdateSessionPolicyValidator : AbstractValidator<UpdateSessionPolicyCommand>
{
    private static readonly string[] ValidStrategies = { "deny_new", "revoke_oldest" };

    public UpdateSessionPolicyValidator()
    {
        RuleFor(x => x.Request.IdleTimeoutMinutes)
            .InclusiveBetween(1, 1440).WithMessage("Idle timeout must be between 1 and 1440 minutes.");

        RuleFor(x => x.Request.AbsoluteTimeoutHours)
            .InclusiveBetween(1, 720).WithMessage("Absolute timeout must be between 1 and 720 hours.");

        RuleFor(x => x.Request.MaxConcurrentSessions)
            .InclusiveBetween(1, 100).WithMessage("Max concurrent sessions must be between 1 and 100.");

        RuleFor(x => x.Request.ConcurrentSessionStrategy!)
            .Must(s => ValidStrategies.Contains(s))
            .WithMessage("Concurrent session strategy must be 'deny_new' or 'revoke_oldest'.")
            .When(x => !string.IsNullOrWhiteSpace(x.Request.ConcurrentSessionStrategy));
    }
}
