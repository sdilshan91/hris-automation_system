// ============================================================================
// US-REC-006: SubmitScorecardValidator unit tests.
//   - BUG-064: an OMITTED overall recommendation is rejected, not silently bound to StrongNoHire (BR-3).
//   - ISSUE-119: a partial scorecard (missing a configured criterion) is rejected (FR-1/FR-3 completeness).
// ============================================================================

using FluentAssertions;
using FluentValidation.TestHelper;
using HRM.Application.Features.Recruitment.Commands;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Validators;
using HRM.Domain.Enums;
using HRM.Domain.Recruitment;

namespace HRM.Tests.Unit;

public sealed class SubmitScorecardValidatorTests
{
    private readonly SubmitScorecardValidator _validator = new();

    private static IReadOnlyList<CriterionRatingInput> AllCriteria(int score = 4) =>
        ScorecardCriteria.Default
            .Select(c => new CriterionRatingInput { CriterionKey = c.Key, Score = score })
            .ToList();

    private static SubmitScorecardCommand Card(
        OverallRecommendation? rec = OverallRecommendation.Hire,
        IReadOnlyList<CriterionRatingInput>? ratings = null) =>
        new(Guid.NewGuid(), rec, "notes", ratings ?? AllCriteria());

    [Fact]
    public void FullyValid_Card_Passes()
    {
        _validator.TestValidate(Card()).IsValid.Should().BeTrue();
    }

    // ── BUG-064: an omitted (null) recommendation → rejected, NOT defaulted to StrongNoHire ──
    [Fact]
    [Trait("Bug", "BUG-064")]
    public void OmittedRecommendation_IsRejected()
    {
        _validator.TestValidate(Card(rec: null))
            .ShouldHaveValidationErrorFor(x => x.OverallRecommendation);
    }

    [Fact]
    [Trait("Bug", "BUG-064")]
    public void PresentRecommendation_IsAccepted()
    {
        _validator.TestValidate(Card(rec: OverallRecommendation.StrongNoHire))
            .ShouldNotHaveValidationErrorFor(x => x.OverallRecommendation); // an EXPLICIT StrongNoHire is fine.
    }

    [Fact]
    [Trait("Bug", "BUG-064")]
    public void OutOfRangeRecommendation_IsRejected()
    {
        _validator.TestValidate(Card(rec: (OverallRecommendation)99)) // the IsInEnum branch (not just NotNull).
            .ShouldHaveValidationErrorFor(x => x.OverallRecommendation);
    }

    // ── ISSUE-119: every configured criterion must be rated ──
    [Fact]
    [Trait("Issue", "ISSUE-119")]
    public void PartialScorecard_MissingACriterion_IsRejected()
    {
        var threeOfFour = ScorecardCriteria.Default.Take(3)
            .Select(c => new CriterionRatingInput { CriterionKey = c.Key, Score = 4 })
            .ToList();

        _validator.TestValidate(Card(ratings: threeOfFour))
            .ShouldHaveValidationErrorFor(x => x.Ratings);
    }

    [Fact]
    [Trait("Issue", "ISSUE-119")]
    public void CompleteScorecard_AllCriteria_Passes()
    {
        _validator.TestValidate(Card(ratings: AllCriteria()))
            .ShouldNotHaveValidationErrorFor(x => x.Ratings);
    }
}
