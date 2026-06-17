// ============================================================================
// US-ADM-004: lifecycle command validator unit tests (FR-1/FR-2/BR-4).
// Reason 10–500 chars; grace period 7–90 days.
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.Tenants.Commands;
using HRM.Application.Features.Tenants.Validators;

namespace HRM.Tests.Unit;

public sealed class TenantLifecycleValidatorTests
{
    private readonly SuspendTenantValidator _suspend = new();
    private readonly TerminateTenantValidator _terminate = new();

    [Fact]
    public void Suspend_Valid_Passes()
        => _suspend.TestValidate(new SuspendTenantCommand(Guid.NewGuid(), "Repeated ToS violations reported."))
            .ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData("")]
    [InlineData("too short")] // 9 chars
    public void Suspend_ShortReason_IsRejected(string reason)
        => _suspend.TestValidate(new SuspendTenantCommand(Guid.NewGuid(), reason))
            .ShouldHaveValidationErrorFor(x => x.Reason);

    [Fact]
    public void Suspend_EmptyTenant_IsRejected()
        => _suspend.TestValidate(new SuspendTenantCommand(Guid.Empty, "A perfectly valid reason here."))
            .ShouldHaveValidationErrorFor(x => x.TenantId);

    [Fact]
    public void Terminate_Valid_Passes()
        => _terminate.TestValidate(new TerminateTenantCommand(Guid.NewGuid(), "Non-payment after dunning.", 30))
            .ShouldNotHaveAnyValidationErrors();

    [Theory]
    [InlineData(7)]
    [InlineData(90)]
    public void Terminate_GraceBoundaries_Pass(int grace)
        => _terminate.TestValidate(new TerminateTenantCommand(Guid.NewGuid(), "Non-payment after dunning.", grace))
            .ShouldNotHaveValidationErrorFor(x => x.GraceDays);

    [Theory]
    [InlineData(6)]
    [InlineData(91)]
    [InlineData(0)]
    public void Terminate_GraceOutOfRange_IsRejected(int grace)
        => _terminate.TestValidate(new TerminateTenantCommand(Guid.NewGuid(), "Non-payment after dunning.", grace))
            .ShouldHaveValidationErrorFor(x => x.GraceDays);
}
