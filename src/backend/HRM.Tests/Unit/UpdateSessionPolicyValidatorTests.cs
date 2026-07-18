// ============================================================================
// US-ADM-006 / ISSUE-009: the session-policy update must enforce the cross-field
// invariant idle <= absolute (normalizing units: idle is MINUTES, absolute is
// HOURS). A policy where a session may idle longer than its absolute lifetime is
// nonsensical and must be rejected 400; a consistent policy validates.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.TenantSettings.Commands;
using HRM.Application.Features.TenantSettings.DTOs;
using HRM.Application.Features.TenantSettings.Validators;

namespace HRM.Tests.Unit;

public sealed class UpdateSessionPolicyValidatorTests
{
    private readonly UpdateSessionPolicyValidator _validator = new();

    private static UpdateSessionPolicyCommand Command(int idleMinutes, int absoluteHours)
        => new(new UpdateSessionPolicyRequest(idleMinutes, absoluteHours, 3, "revoke_oldest"));

    [Fact]
    public void IdleExceedsAbsolute_IsRejected()
    {
        // idle 1440 min (24 h) > absolute 1 h → invalid once units are normalized.
        var result = _validator.TestValidate(Command(idleMinutes: 1440, absoluteHours: 1));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage == "Idle timeout must not exceed the absolute timeout.");
    }

    [Fact]
    public void IdleEqualToAbsolute_IsAllowed()
    {
        // idle 60 min == absolute 1 h (60 min) → boundary, allowed.
        var result = _validator.TestValidate(Command(idleMinutes: 60, absoluteHours: 1));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void IdleWithinAbsolute_IsAllowed()
    {
        // idle 60 min < absolute 24 h → valid.
        var result = _validator.TestValidate(Command(idleMinutes: 60, absoluteHours: 24));

        result.IsValid.Should().BeTrue();
    }
}
