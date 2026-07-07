using System.Text.Json;
using FluentAssertions;
using HRM.Application.Features.Recruitment.DTOs;

namespace HRM.Tests.Unit;

/// <summary>
/// ISSUE-244 / NFR-5: the internal, tenant-scoped resume blob path (<c>ResumeStorageKey</c>) must never
/// leave the API. It was removed from <see cref="ApplicantDto"/> (and its <c>ToDto</c> mapping) while the
/// entity field was kept. This is a compile-safe wire-contract guard: if anyone re-adds the property, the
/// reflection assertion fails immediately.
///
/// Pre-fix reasoning: the DTO previously exposed <c>ResumeStorageKey</c>, so both the reflection lookup and
/// the serialized-JSON check below would have found it.
/// </summary>
public sealed class ApplicantDtoResumeKeyTests
{
    [Fact]
    public void ApplicantDto_NoResumeStorageKey_ISSUE244()
    {
        typeof(ApplicantDto).GetProperty("ResumeStorageKey")
            .Should().BeNull("the internal blob storage key must not be a wire field on ApplicantDto");
    }

    [Fact]
    public void ApplicantDto_SerializedJson_HasNoResumeStorageKey_ISSUE244()
    {
        var dto = new ApplicantDto
        {
            Id = Guid.NewGuid(),
            VacancyId = Guid.NewGuid(),
            ApplicationReferenceNumber = "APP-2026-0001",
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = "ada@example.com",
            ResumeFileName = "ada-cv.pdf",
        };

        var json = JsonSerializer.Serialize(
            dto, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        json.Should().NotContainEquivalentOf("resumeStorageKey",
            "the internal storage key must not appear anywhere on the serialized wire payload");
        // Sanity: the safe, intended fields are still present so we know the DTO actually serialized.
        json.Should().Contain("resumeFileName");
    }
}
