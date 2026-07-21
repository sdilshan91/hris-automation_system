// ============================================================================
// US-ONB-004: IssueAssetsCommand validator tests (structural rules, §7).
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.Onboarding.Commands;
using HRM.Application.Features.Onboarding.Validators;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class IssueAssetsValidatorTests
{
    private readonly IssueAssetsValidator _validator = new();

    private static IssueAssetsCommand Cmd(params IssueAssetCommandLine[] lines) =>
        new(Guid.NewGuid(), null, lines, null, null, null, 0);

    private static IssueAssetCommandLine Line(
        string tag = "LAP-1", string type = "Laptop", AssetCondition condition = AssetCondition.New,
        DateOnly? issueDate = null, string? notes = null) =>
        new(null, type, tag, null, null, null, condition, issueDate ?? DateOnly.FromDateTime(DateTime.UtcNow), notes);

    [Fact]
    public void Valid_command_passes()
    {
        var result = _validator.TestValidate(Cmd(Line()));
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Empty_employee_id_fails()
    {
        var result = _validator.TestValidate(new IssueAssetsCommand(
            Guid.Empty, null, new[] { Line() }, null, null, null, 0));
        result.ShouldHaveValidationErrorFor(x => x.EmployeeId);
    }

    [Fact]
    public void No_assets_fails()
    {
        var result = _validator.TestValidate(Cmd());
        result.ShouldHaveValidationErrorFor(x => x.Assets);
    }

    [Fact]
    public void Future_issue_date_fails()
    {
        var result = _validator.TestValidate(Cmd(Line(issueDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)))));
        result.ShouldHaveValidationErrorFor("Assets[0].IssueDate");
    }

    [Fact]
    public void Missing_tag_fails()
    {
        var result = _validator.TestValidate(Cmd(Line(tag: "")));
        result.ShouldHaveValidationErrorFor("Assets[0].AssetTag");
    }

    [Fact]
    public void Missing_type_fails()
    {
        var result = _validator.TestValidate(Cmd(Line(type: "")));
        result.ShouldHaveValidationErrorFor("Assets[0].AssetType");
    }

    [Fact]
    public void Notes_over_500_chars_fails()
    {
        var result = _validator.TestValidate(Cmd(Line(notes: new string('x', 501))));
        result.ShouldHaveValidationErrorFor("Assets[0].Notes");
    }
}
