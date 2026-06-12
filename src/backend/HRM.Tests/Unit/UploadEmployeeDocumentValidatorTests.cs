// ============================================================================
// US-CHR-008: UploadEmployeeDocumentValidator -- Unit Tests
// Tests FluentValidation rules on UploadEmployeeDocumentCommand:
// EmployeeId required, FileName required + max 255, Category required +
// must be one of the allowed values, Description max 2000, ExpiryDate must
// be in the future when provided.
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Employees.Commands;
using HRM.Application.Features.Employees.Validators;

namespace HRM.Tests.Unit;

public sealed class UploadEmployeeDocumentValidatorTests
{
    private readonly UploadEmployeeDocumentValidator _validator = new();

    private static UploadEmployeeDocumentCommand MakeCommand(
        Guid? employeeId = null,
        string fileName = "test.pdf",
        string contentType = "application/pdf",
        long fileSize = 1024,
        string category = "Contract",
        string? description = null,
        DateTime? expiryDate = null)
    {
        return new UploadEmployeeDocumentCommand(
            EmployeeId: employeeId ?? Guid.NewGuid(),
            FileStream: new MemoryStream(new byte[1]),
            FileName: fileName,
            ContentType: contentType,
            FileSize: fileSize,
            Category: category,
            Description: description,
            ExpiryDate: expiryDate);
    }

    // ── EmployeeId ────────────────────────────────────────────────────

    [Fact]
    public void EmployeeId_Empty_ShouldHaveError()
    {
        var cmd = MakeCommand(employeeId: Guid.Empty);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.EmployeeId)
            .WithErrorMessage("Employee ID is required.");
    }

    [Fact]
    public void EmployeeId_Valid_ShouldNotHaveError()
    {
        var cmd = MakeCommand(employeeId: Guid.NewGuid());
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.EmployeeId);
    }

    // ── FileName ──────────────────────────────────────────────────────

    [Fact]
    public void FileName_Empty_ShouldHaveError()
    {
        var cmd = MakeCommand(fileName: "");
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.FileName)
            .WithErrorMessage("File name is required.");
    }

    [Fact]
    public void FileName_TooLong_ShouldHaveError()
    {
        var longName = new string('a', 256);
        var cmd = MakeCommand(fileName: longName);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.FileName)
            .WithErrorMessage("File name must not exceed 255 characters.");
    }

    [Fact]
    public void FileName_Exactly255_ShouldNotHaveError()
    {
        var name = new string('a', 255);
        var cmd = MakeCommand(fileName: name);
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.FileName);
    }

    // ── Category ──────────────────────────────────────────────────────

    [Fact]
    public void Category_Empty_ShouldHaveError()
    {
        var cmd = MakeCommand(category: "");
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Category);
    }

    [Theory]
    [InlineData("Contract")]
    [InlineData("ID")]
    [InlineData("Certificate")]
    [InlineData("Other")]
    public void Category_Valid_ShouldNotHaveError(string category)
    {
        var cmd = MakeCommand(category: category);
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.Category);
    }

    [Theory]
    [InlineData("InvalidCategory")]
    [InlineData("contract")] // case-sensitive in validator (Must() check uses Contains which is case-sensitive)
    [InlineData("CERTIFICATE")]
    public void Category_Invalid_ShouldHaveError(string category)
    {
        var cmd = MakeCommand(category: category);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Category)
            .WithErrorMessage("Invalid category. Allowed: Contract, ID, Certificate, Other.");
    }

    // ── Description ───────────────────────────────────────────────────

    [Fact]
    public void Description_Null_ShouldNotHaveError()
    {
        var cmd = MakeCommand(description: null);
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Description_TooLong_ShouldHaveError()
    {
        var longDesc = new string('x', 2001);
        var cmd = MakeCommand(description: longDesc);
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.Description)
            .WithErrorMessage("Description must not exceed 2000 characters.");
    }

    [Fact]
    public void Description_Exactly2000_ShouldNotHaveError()
    {
        var desc = new string('x', 2000);
        var cmd = MakeCommand(description: desc);
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.Description);
    }

    // ── ExpiryDate ────────────────────────────────────────────────────

    [Fact]
    public void ExpiryDate_Null_ShouldNotHaveError()
    {
        var cmd = MakeCommand(expiryDate: null);
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.ExpiryDate);
    }

    [Fact]
    public void ExpiryDate_InTheFuture_ShouldNotHaveError()
    {
        var cmd = MakeCommand(expiryDate: DateTime.UtcNow.AddDays(30));
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveValidationErrorFor(x => x.ExpiryDate);
    }

    [Fact]
    public void ExpiryDate_InThePast_ShouldHaveError()
    {
        var cmd = MakeCommand(expiryDate: DateTime.UtcNow.AddDays(-1));
        var result = _validator.TestValidate(cmd);
        result.ShouldHaveValidationErrorFor(x => x.ExpiryDate)
            .WithErrorMessage("Expiry date must be in the future.");
    }

    // ── Full valid command ────────────────────────────────────────────

    [Fact]
    public void FullyValid_Command_ShouldHaveNoErrors()
    {
        var cmd = MakeCommand(
            employeeId: Guid.NewGuid(),
            fileName: "contract.pdf",
            category: "Contract",
            description: "Employment contract",
            expiryDate: DateTime.UtcNow.AddYears(1));
        var result = _validator.TestValidate(cmd);
        result.ShouldNotHaveAnyValidationErrors();
    }
}
