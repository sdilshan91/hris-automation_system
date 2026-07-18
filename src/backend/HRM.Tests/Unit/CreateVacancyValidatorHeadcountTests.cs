// ============================================================================
// US-REC-001 / ISSUE-096: FR-1 lists headcount as a required integer >= 1. An omitted
// headcount used to bind to a default of 1 and silently pass; it must now be rejected
// (400 via the FluentValidation pipeline) as "required". A present value >= 1 is accepted;
// an explicit value < 1 remains rejected with the at-least-1 message.
// ============================================================================

using FluentValidation.TestHelper;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.Validators;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class CreateVacancyValidatorHeadcountTests
{
    private readonly CreateVacancyValidator _create = new();
    private readonly UpdateVacancyValidator _update = new();

    private static CreateVacancyCommand CreateCommand(int? headcount) => new(
        Title: "Backend Engineer",
        DepartmentId: null, JobTitleId: null, LocationId: null, HiringManagerId: null,
        EmploymentType: EmploymentType.FullTime,
        Headcount: headcount,
        SalaryMin: null, SalaryMax: null, SalaryCurrency: null,
        Description: "<p>x</p>", Qualifications: null, ApplicationDeadline: null,
        PublishToPublicCareers: true);

    private static UpdateVacancyCommand UpdateCommand(int? headcount) => new(
        VacancyId: Guid.NewGuid(),
        Title: "Backend Engineer",
        DepartmentId: null, JobTitleId: null, LocationId: null, HiringManagerId: null,
        EmploymentType: EmploymentType.FullTime,
        Headcount: headcount,
        SalaryMin: null, SalaryMax: null, SalaryCurrency: null,
        Description: "<p>x</p>", Qualifications: null, ApplicationDeadline: null,
        PublishToPublicCareers: true);

    [Fact]
    public void Create_OmittedHeadcount_IsRejectedAsRequired()
    {
        var result = _create.TestValidate(CreateCommand(headcount: null));

        result.ShouldHaveValidationErrorFor(x => x.Headcount)
            .WithErrorMessage("Headcount is required.");
    }

    [Fact]
    public void Create_HeadcountOfThree_IsAccepted()
    {
        var result = _create.TestValidate(CreateCommand(headcount: 3));

        result.ShouldNotHaveValidationErrorFor(x => x.Headcount);
    }

    [Fact]
    public void Create_HeadcountBelowOne_IsRejectedAsAtLeastOne()
    {
        var result = _create.TestValidate(CreateCommand(headcount: 0));

        result.ShouldHaveValidationErrorFor(x => x.Headcount)
            .WithErrorMessage("Headcount must be at least 1.");
    }

    [Fact]
    public void Update_OmittedHeadcount_IsRejectedAsRequired()
    {
        var result = _update.TestValidate(UpdateCommand(headcount: null));

        result.ShouldHaveValidationErrorFor(x => x.Headcount)
            .WithErrorMessage("Headcount is required.");
    }

    [Fact]
    public void Update_HeadcountOfThree_IsAccepted()
    {
        var result = _update.TestValidate(UpdateCommand(headcount: 3));

        result.ShouldNotHaveValidationErrorFor(x => x.Headcount);
    }
}
