// ============================================================================
// US-ONB-002: validator unit tests for the assign + modify commands.
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.Onboarding.Commands;
using HRM.Application.Features.Onboarding.DTOs;
using HRM.Application.Features.Onboarding.Validators;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class OnboardingChecklistValidatorTests
{
    private readonly AssignChecklistValidator _assign = new();
    private readonly ModifyAssignedChecklistValidator _modify = new();

    private static AssignChecklistCommand AssignCmd(
        Guid? employeeId = null, Guid? templateId = null,
        IReadOnlyList<AssignChecklistAdHocTask>? adhoc = null) =>
        new(employeeId ?? Guid.NewGuid(), templateId ?? Guid.NewGuid(), null,
            ChecklistAssignmentMode.Replace, adhoc ?? Array.Empty<AssignChecklistAdHocTask>(), null);

    [Fact]
    public void Assign_valid_command_passes()
    {
        _assign.TestValidate(AssignCmd()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Assign_requires_employee_id()
    {
        _assign.TestValidate(AssignCmd(employeeId: Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.EmployeeId);
    }

    [Fact]
    public void Assign_requires_template_id()
    {
        _assign.TestValidate(AssignCmd(templateId: Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.TemplateId);
    }

    [Fact]
    public void Assign_rejects_short_adhoc_title()
    {
        var cmd = AssignCmd(adhoc: new[]
        {
            new AssignChecklistAdHocTask("ab", null, null, OnboardingResponsibleRole.HR, null, 0, false, 0),
        });

        _assign.TestValidate(cmd)
            .ShouldHaveValidationErrorFor("AdditionalTasks[0].Title");
    }

    [Fact]
    public void Assign_rejects_negative_due_offset()
    {
        var cmd = AssignCmd(adhoc: new[]
        {
            new AssignChecklistAdHocTask("Valid title", null, null, OnboardingResponsibleRole.HR, null, -1, false, 0),
        });

        _assign.TestValidate(cmd)
            .ShouldHaveValidationErrorFor("AdditionalTasks[0].DueOffsetDays");
    }

    [Fact]
    public void Modify_requires_at_least_one_change()
    {
        var cmd = new ModifyAssignedChecklistCommand(
            Guid.NewGuid(), Array.Empty<AssignChecklistAdHocTask>(), Array.Empty<ModifyChecklistTaskChange>());

        _modify.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x);
    }

    [Fact]
    public void Modify_requires_checklist_instance_id()
    {
        var cmd = new ModifyAssignedChecklistCommand(
            Guid.Empty, Array.Empty<AssignChecklistAdHocTask>(),
            new[] { new ModifyChecklistTaskChange(Guid.NewGuid(), null, true) });

        _modify.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.ChecklistInstanceId);
    }

    [Fact]
    public void Modify_valid_command_passes()
    {
        var cmd = new ModifyAssignedChecklistCommand(
            Guid.NewGuid(), Array.Empty<AssignChecklistAdHocTask>(),
            new[] { new ModifyChecklistTaskChange(Guid.NewGuid(), DateTime.UtcNow.AddDays(1), false) });

        _modify.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }
}
