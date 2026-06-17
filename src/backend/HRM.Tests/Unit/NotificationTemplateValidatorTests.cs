// US-NTF-002: validator tests for the save / test-email commands (FR-1/BR-3/FR-8).

using FluentValidation.TestHelper;
using HRM.Application.Features.NotificationTemplates.Commands;
using HRM.Application.Features.NotificationTemplates.DTOs;
using HRM.Application.Features.NotificationTemplates.Validators;

namespace HRM.Tests.Unit;

public sealed class NotificationTemplateValidatorTests
{
    private readonly SaveTemplateValidator _save = new();
    private readonly SendTestEmailValidator _test = new();

    [Fact]
    public void Save_RequiresSubjectAndBothBodies()
    {
        var cmd = new SaveTemplateCommand("leave_approved", "en", new TemplateBodyRequest("", "", ""));

        var result = _save.TestValidate(cmd);

        result.ShouldHaveValidationErrorFor("Body.Subject");
        result.ShouldHaveValidationErrorFor("Body.BodyHtml");
        result.ShouldHaveValidationErrorFor("Body.BodyText");
    }

    [Fact]
    public void Save_Passes_WhenComplete()
    {
        var cmd = new SaveTemplateCommand("leave_approved", "en",
            new TemplateBodyRequest("Subject", "<p>html</p>", "text"));

        _save.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void TestEmail_RequiresValidRecipient()
    {
        var bad = new SendTestEmailCommand("leave_approved", new TestEmailRequest("not-an-email", "en"));

        _test.TestValidate(bad).ShouldHaveValidationErrorFor("Request.RecipientEmail");
    }

    [Fact]
    public void TestEmail_Passes_WithValidRecipient()
    {
        var ok = new SendTestEmailCommand("leave_approved", new TestEmailRequest("admin@acme.test", "en"));

        _test.TestValidate(ok).ShouldNotHaveAnyValidationErrors();
    }
}
