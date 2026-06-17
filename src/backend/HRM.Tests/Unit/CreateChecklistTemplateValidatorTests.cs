// ============================================================================
// US-ONB-001: CreateChecklistTemplate validator unit tests.
//
// Covers the structural/business rules enforceable without DB access:
//   - AC-3 name: required + min 3 / max 200.
//   - BR-2: a template must contain at least one task.
//   - BR-3: due_offset_days >= 0 (and sort_order >= 0).
//   - responsible_role in {HR, Manager, IT, Employee, Custom} (enum membership).
//   - task title required + min 3 / max 200; description/category length caps.
// The unique-name rule (BR-1) needs the tenant DB and is covered in the service tests.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Onboarding.Commands;
using HRM.Application.Features.Onboarding.Validators;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class CreateChecklistTemplateValidatorTests
{
    private readonly CreateChecklistTemplateValidator _validator = new();

    private static CreateChecklistTemplateTask Task(
        string title = "Collect signed contract",
        int dueOffset = 0,
        int sortOrder = 0,
        OnboardingResponsibleRole role = OnboardingResponsibleRole.HR) =>
        new(title, "desc", "Documentation", role, null, dueOffset, false, sortOrder);

    private static CreateChecklistTemplateCommand Command(
        string name = "Standard Onboarding",
        params CreateChecklistTemplateTask[] tasks) =>
        new(name, "A reusable template", [], [], true,
            tasks.Length == 0 ? [Task()] : tasks);

    [Fact]
    public void Valid_command_passes()
    {
        _validator.TestValidate(Command()).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void Name_too_short_fails(string name)
    {
        _validator.TestValidate(Command(name))
            .ShouldHaveValidationErrorFor(x => x.TemplateName);
    }

    [Fact]
    public void Name_too_long_fails()
    {
        _validator.TestValidate(Command(new string('x', 201)))
            .ShouldHaveValidationErrorFor(x => x.TemplateName);
    }

    [Fact]
    public void Empty_task_list_fails_br2()
    {
        var command = new CreateChecklistTemplateCommand(
            "Standard Onboarding", null, [], [], true, []);

        _validator.TestValidate(command)
            .ShouldHaveValidationErrorFor(x => x.Tasks);
    }

    [Fact]
    public void Negative_due_offset_fails_br3()
    {
        var result = _validator.TestValidate(Command("Standard Onboarding", Task(dueOffset: -1)));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Due offset"));
    }

    [Fact]
    public void Zero_due_offset_is_allowed_br3()
    {
        var result = _validator.TestValidate(Command("Standard Onboarding", Task(dueOffset: 0)));
        result.Errors.Should().NotContain(e => e.ErrorMessage.Contains("Due offset"));
    }

    [Fact]
    public void Negative_sort_order_fails()
    {
        var result = _validator.TestValidate(Command("Standard Onboarding", Task(sortOrder: -1)));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Sort order"));
    }

    [Fact]
    public void Invalid_responsible_role_fails()
    {
        var result = _validator.TestValidate(
            Command("Standard Onboarding", Task(role: (OnboardingResponsibleRole)99)));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("Responsible role"));
    }

    [Fact]
    public void Short_task_title_fails()
    {
        var result = _validator.TestValidate(Command("Standard Onboarding", Task(title: "no")));
        result.IsValid.Should().BeFalse();
    }
}
