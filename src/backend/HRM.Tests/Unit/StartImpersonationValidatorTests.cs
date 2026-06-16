// ============================================================================
// US-ADM-003: StartImpersonationCommand validator unit tests (FR-1/BR-4).
// Shape-level checks only — reason 10–500 chars, target ids non-empty. The
// cross-entity rules (tenant exists/not-terminated, membership active, BR-2/3)
// live in ImpersonationService and are covered by the integration tests.
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.Impersonation.Commands;
using HRM.Application.Features.Impersonation.Validators;

namespace HRM.Tests.Unit;

public sealed class StartImpersonationValidatorTests
{
    private readonly StartImpersonationValidator _validator = new();

    private static StartImpersonationCommand Cmd(
        Guid? targetUser = null, Guid? targetTenant = null,
        string reason = "Investigating a reported payroll bug for the customer.")
        => new(targetUser ?? Guid.NewGuid(), targetTenant ?? Guid.NewGuid(), reason);

    [Fact]
    public void Valid_Command_Passes()
        => _validator.TestValidate(Cmd()).ShouldNotHaveAnyValidationErrors();

    [Fact]
    public void EmptyTargetUser_IsRejected()
        => _validator.TestValidate(Cmd(targetUser: Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.TargetUserId);

    [Fact]
    public void EmptyTargetTenant_IsRejected()
        => _validator.TestValidate(Cmd(targetTenant: Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.TargetTenantId);

    [Theory]
    [InlineData("")]            // empty
    [InlineData("too short")]   // 9 chars, < 10 (BR-4)
    public void ShortOrEmptyReason_IsRejected(string reason)
        => _validator.TestValidate(Cmd(reason: reason))
            .ShouldHaveValidationErrorFor(x => x.Reason);

    [Fact]
    public void Reason_ExactlyTen_Passes()
        => _validator.TestValidate(Cmd(reason: new string('a', 10)))
            .ShouldNotHaveValidationErrorFor(x => x.Reason);

    [Fact]
    public void Reason_TooLong_IsRejected()
        => _validator.TestValidate(Cmd(reason: new string('a', 501)))
            .ShouldHaveValidationErrorFor(x => x.Reason);
}
