// ============================================================================
// US-REC-001: Vacancy status-transition + slug generation — unit tests.
// Covers FR-2 (allowed/rejected transitions) on the centralised
// VacancyStatusTransitions table, and FR-5 slug generation rules. These are
// pure domain helpers shared by the service, validators, and tests.
// ============================================================================

using FluentAssertions;
using HRM.Domain.Entities;
using HRM.Domain.Enums;

namespace HRM.Tests.Unit;

public sealed class VacancyStatusTransitionTests
{
    // ── FR-2 allowed transitions ─────────────────────────────────────
    [Theory]
    [InlineData(VacancyStatus.Draft, VacancyStatus.Open)]
    [InlineData(VacancyStatus.Draft, VacancyStatus.Cancelled)]
    [InlineData(VacancyStatus.Open, VacancyStatus.OnHold)]
    [InlineData(VacancyStatus.Open, VacancyStatus.Closed)]
    [InlineData(VacancyStatus.Open, VacancyStatus.Cancelled)]
    [InlineData(VacancyStatus.OnHold, VacancyStatus.Open)]
    [InlineData(VacancyStatus.OnHold, VacancyStatus.Closed)]
    [InlineData(VacancyStatus.OnHold, VacancyStatus.Cancelled)]
    public void IsAllowed_ReturnsTrue_ForLegalTransitions(VacancyStatus from, VacancyStatus to)
    {
        VacancyStatusTransitions.IsAllowed(from, to).Should().BeTrue();
    }

    // ── FR-2 rejected transitions ────────────────────────────────────
    [Theory]
    [InlineData(VacancyStatus.Draft, VacancyStatus.OnHold)]
    [InlineData(VacancyStatus.Draft, VacancyStatus.Closed)]
    [InlineData(VacancyStatus.Open, VacancyStatus.Draft)]
    [InlineData(VacancyStatus.OnHold, VacancyStatus.Draft)]
    [InlineData(VacancyStatus.Closed, VacancyStatus.Open)]      // terminal
    [InlineData(VacancyStatus.Closed, VacancyStatus.OnHold)]    // terminal
    [InlineData(VacancyStatus.Closed, VacancyStatus.Cancelled)] // terminal
    [InlineData(VacancyStatus.Cancelled, VacancyStatus.Open)]   // terminal
    [InlineData(VacancyStatus.Cancelled, VacancyStatus.Draft)]  // terminal
    public void IsAllowed_ReturnsFalse_ForIllegalTransitions(VacancyStatus from, VacancyStatus to)
    {
        VacancyStatusTransitions.IsAllowed(from, to).Should().BeFalse();
    }

    [Theory]
    [InlineData(VacancyStatus.Draft)]
    [InlineData(VacancyStatus.Open)]
    [InlineData(VacancyStatus.OnHold)]
    [InlineData(VacancyStatus.Closed)]
    [InlineData(VacancyStatus.Cancelled)]
    public void IsAllowed_ReturnsFalse_ForSelfTransition(VacancyStatus status)
    {
        VacancyStatusTransitions.IsAllowed(status, status).Should().BeFalse();
    }

    [Fact]
    public void AllowedFrom_Terminal_IsEmpty()
    {
        VacancyStatusTransitions.AllowedFrom(VacancyStatus.Closed).Should().BeEmpty();
        VacancyStatusTransitions.AllowedFrom(VacancyStatus.Cancelled).Should().BeEmpty();
    }

    [Fact]
    public void AllowedFrom_Open_ContainsOnHoldClosedCancelled()
    {
        VacancyStatusTransitions.AllowedFrom(VacancyStatus.Open)
            .Should().BeEquivalentTo(new[]
            {
                VacancyStatus.OnHold, VacancyStatus.Closed, VacancyStatus.Cancelled,
            });
    }
}

public sealed class VacancySlugGeneratorTests
{
    [Theory]
    [InlineData("Senior Software Engineer", "senior-software-engineer")]
    [InlineData("  Trimmed  Title  ", "trimmed-title")]
    [InlineData("C# / .NET Developer!!!", "c-net-developer")]
    [InlineData("Café Manager (Évry)", "cafe-manager-evry")]   // accent folding
    [InlineData("Multiple---Hyphens", "multiple-hyphens")]
    public void Generate_ProducesSeoFriendlySlug(string title, string expected)
    {
        VacancySlugGenerator.Generate(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    [InlineData(null)]
    public void Generate_FallsBackToVacancy_WhenNothingUsable(string? title)
    {
        VacancySlugGenerator.Generate(title).Should().Be("vacancy");
    }

    [Fact]
    public void Generate_IsLowercaseAndAsciiOnly()
    {
        var slug = VacancySlugGenerator.Generate("HÉLLO Wörld 2026");
        slug.Should().MatchRegex("^[a-z0-9-]+$");
    }

    [Fact]
    public void Generate_TruncatesLongTitles()
    {
        var slug = VacancySlugGenerator.Generate(new string('a', 200));
        slug.Length.Should().BeLessThanOrEqualTo(80);
    }
}
