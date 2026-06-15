// ============================================================================
// US-REC-002: SubmitApplicationValidator -- Unit Tests
// Tests FluentValidation rules on SubmitApplicationCommand: required name/email,
// valid email, cover-letter <= 2000 (BR-1), resume MIME type in PDF/DOC/DOCX (BR-4),
// resume size <= 25 MB (AC-2), and the internal-application linked-employee rule (FR-8/BR-5).
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.Validators;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class SubmitApplicationValidatorTests
{
    private readonly SubmitApplicationValidator _validator = new();

    private static SubmitApplicationCommand MakeCommand(
        Guid? vacancyId = null,
        string firstName = "Ada",
        string lastName = "Lovelace",
        string email = "ada@example.com",
        string? phone = null,
        string? coverLetter = null,
        string resumeFileName = "resume.pdf",
        string resumeContentType = "application/pdf",
        long resumeFileSize = 1024,
        ApplicationSource source = ApplicationSource.Public,
        Guid? linkedEmployeeId = null)
    {
        return new SubmitApplicationCommand(
            VacancyId: vacancyId ?? Guid.NewGuid(),
            FirstName: firstName,
            LastName: lastName,
            Email: email,
            Phone: phone,
            CoverLetter: coverLetter,
            ResumeStream: new MemoryStream(new byte[1]),
            ResumeFileName: resumeFileName,
            ResumeContentType: resumeContentType,
            ResumeFileSize: resumeFileSize,
            Source: source,
            LinkedEmployeeId: linkedEmployeeId);
    }

    // ── Required fields ───────────────────────────────────────────────

    [Fact]
    public void FirstName_Empty_ShouldHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(firstName: ""));
        result.ShouldHaveValidationErrorFor(x => x.FirstName);
    }

    [Fact]
    public void LastName_Empty_ShouldHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(lastName: ""));
        result.ShouldHaveValidationErrorFor(x => x.LastName);
    }

    [Fact]
    public void Email_Empty_ShouldHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(email: ""));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Theory]
    [InlineData("not-an-email")]   // no @ at all
    [InlineData("@nope.com")]      // @ at the start
    [InlineData("nope.com@")]      // @ at the end
    public void Email_Invalid_ShouldHaveError(string email)
    {
        // FluentValidation's default EmailAddress() validator is intentionally lenient (it rejects only
        // a missing/edge @, matching ASP.NET Core's EmailAddressAttribute) — these are the cases it catches.
        var result = _validator.TestValidate(MakeCommand(email: email));
        result.ShouldHaveValidationErrorFor(x => x.Email);
    }

    [Fact]
    public void Email_Valid_ShouldNotHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(email: "valid.person@company.co"));
        result.ShouldNotHaveValidationErrorFor(x => x.Email);
    }

    // ── Cover letter (BR-1, <= 2000) ──────────────────────────────────

    [Fact]
    public void CoverLetter_Exactly2000_ShouldNotHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(coverLetter: new string('x', 2000)));
        result.ShouldNotHaveValidationErrorFor(x => x.CoverLetter);
    }

    [Fact]
    public void CoverLetter_TooLong_ShouldHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(coverLetter: new string('x', 2001)));
        result.ShouldHaveValidationErrorFor(x => x.CoverLetter);
    }

    // ── Resume MIME type (BR-4) ───────────────────────────────────────

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("application/msword")]
    public void Resume_AllowedMimeType_ShouldNotHaveError(string contentType)
    {
        var result = _validator.TestValidate(MakeCommand(resumeContentType: contentType));
        result.ShouldNotHaveValidationErrorFor(x => x.ResumeContentType);
    }

    [Theory]
    [InlineData("application/x-msdownload")] // .exe renamed to .pdf -> wrong content type
    [InlineData("image/png")]
    [InlineData("text/plain")]
    [InlineData("")]
    public void Resume_DisallowedMimeType_ShouldHaveError(string contentType)
    {
        var result = _validator.TestValidate(MakeCommand(resumeContentType: contentType));
        result.ShouldHaveValidationErrorFor(x => x.ResumeContentType)
            .WithErrorMessage("Resume must be a PDF, DOC, or DOCX file.");
    }

    // ── Resume size (AC-2, <= 25 MB) ──────────────────────────────────

    [Fact]
    public void Resume_Exactly25Mb_ShouldNotHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(resumeFileSize: SubmitApplicationValidator.MaxResumeSizeBytes));
        result.ShouldNotHaveValidationErrorFor(x => x.ResumeFileSize);
    }

    [Fact]
    public void Resume_Over25Mb_ShouldHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(resumeFileSize: SubmitApplicationValidator.MaxResumeSizeBytes + 1));
        result.ShouldHaveValidationErrorFor(x => x.ResumeFileSize)
            .WithErrorMessage("Resume exceeds the 25 MB limit.");
    }

    [Fact]
    public void Resume_ZeroSize_ShouldHaveError()
    {
        var result = _validator.TestValidate(MakeCommand(resumeFileSize: 0));
        result.ShouldHaveValidationErrorFor(x => x.ResumeFileSize);
    }

    // ── Internal application linked employee (FR-8 / BR-5) ────────────

    [Fact]
    public void Internal_WithoutLinkedEmployee_ShouldHaveError()
    {
        var result = _validator.TestValidate(
            MakeCommand(source: ApplicationSource.Internal, linkedEmployeeId: null));
        result.ShouldHaveValidationErrorFor(x => x.LinkedEmployeeId);
    }

    [Fact]
    public void Internal_WithLinkedEmployee_ShouldNotHaveError()
    {
        var result = _validator.TestValidate(
            MakeCommand(source: ApplicationSource.Internal, linkedEmployeeId: Guid.NewGuid()));
        result.ShouldNotHaveValidationErrorFor(x => x.LinkedEmployeeId);
    }

    [Fact]
    public void Public_WithoutLinkedEmployee_ShouldNotHaveError()
    {
        var result = _validator.TestValidate(
            MakeCommand(source: ApplicationSource.Public, linkedEmployeeId: null));
        result.ShouldNotHaveValidationErrorFor(x => x.LinkedEmployeeId);
    }

    // ── Fully valid ───────────────────────────────────────────────────

    [Fact]
    public void FullyValid_PublicCommand_ShouldHaveNoErrors()
    {
        var result = _validator.TestValidate(MakeCommand(
            firstName: "Grace", lastName: "Hopper", email: "grace@navy.mil",
            phone: "+1-555-0100", coverLetter: "Interested in the role.",
            resumeFileName: "grace_hopper_cv.pdf", resumeContentType: "application/pdf", resumeFileSize: 2048));
        result.ShouldNotHaveAnyValidationErrors();
    }
}
