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

    // ── BUG-441: the replace-mode task set ────────────────────────────

    private static AssignChecklistResolvedTask ResolvedTask(
        string title = "Sign employment contract", DateOnly? dueDate = null) =>
        new(Guid.NewGuid(), title, null, null, OnboardingResponsibleRole.HR,
            dueDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)), true, 0);

    [Fact]
    public void Assign_with_resolved_tasks_only_passes()
    {
        var cmd = AssignCmd() with { ResolvedTasks = new[] { ResolvedTask() } };

        _assign.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    /// <summary>
    /// The two task-set fields are mutually exclusive. Picking a winner silently would discard one set of
    /// tasks the HR officer actually entered — the same invisible data loss BUG-441 was.
    /// </summary>
    [Fact]
    public void Assign_rejects_both_task_sets_together()
    {
        var cmd = AssignCmd(adhoc: new[]
        {
            new AssignChecklistAdHocTask("Order security badge", null, null, OnboardingResponsibleRole.HR, null, 2, false, 0),
        }) with
        { ResolvedTasks = new[] { ResolvedTask() } };

        _assign.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.AdditionalTasks);
    }

    /// <summary>
    /// A resolved row without a due date is a 400, never <c>default(DateOnly)</c>. Binding a missing date to
    /// a silent default is exactly how BUG-441 put every task on <c>startDate + 0</c>.
    /// </summary>
    [Fact]
    public void Assign_rejects_resolved_task_without_a_due_date()
    {
        var cmd = AssignCmd() with
        {
            ResolvedTasks = new[]
            {
                new AssignChecklistResolvedTask(
                    null, "Sign employment contract", null, null, OnboardingResponsibleRole.HR, null, false, 0),
            },
        };

        _assign.TestValidate(cmd).ShouldHaveValidationErrorFor("ResolvedTasks[0].DueDate");
    }

    [Fact]
    public void Assign_rejects_short_resolved_task_title()
    {
        var cmd = AssignCmd() with { ResolvedTasks = new[] { ResolvedTask(title: "ab") } };

        _assign.TestValidate(cmd).ShouldHaveValidationErrorFor("ResolvedTasks[0].Title");
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
            new[] { new ModifyChecklistTaskChange(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), false) });

        _modify.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }
}
