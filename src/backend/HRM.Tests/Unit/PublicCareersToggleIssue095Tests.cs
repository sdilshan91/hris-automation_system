// ============================================================================
// ISSUE-095 (US-REC-002 BR-5) — the anonymous PUBLIC apply path must honour the
// tenant's PublicCareersEnabled toggle.
//
// ApplicantService.SubmitAsync, for ApplicationSource.Public ONLY, reads the
// tenant's PublicCareersEnabled flag and returns 404 vacancy_not_found when it is
// off — mirroring the public list/detail (which hide vacancies when careers are
// disabled) rather than disclosing that a vacancy exists. The internal /
// authenticated apply path is unaffected by the toggle.
//
// PRE-FIX: SubmitAsync had no toggle check, so a Public submission against a
// disabled tenant fell through to the normal vacancy lookup and SUCCEEDED — the
// CareersDisabled test's 404 assertion FAILS. POST-FIX it returns 404 first.
//
// These tests drive the REAL ApplicantService on the same InMemory harness as
// ApplicantSubmissionAuditTests. Both cases seed the SAME published, open vacancy;
// the ONLY difference is PublicCareersEnabled, so the change in outcome is keyed
// squarely on the toggle (no theater). InMemory is faithful here — the toggle is a
// plain column read + a branch.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class PublicCareersToggleIssue095Tests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly Guid _vacancyId = Guid.NewGuid();

    public PublicCareersToggleIssue095Tests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private ApplicantService Service(ICurrentUser user) => new(
        Db(),
        _tenantContext,
        user,
        new InMemoryFileStorage(),
        new AllowWithLogVirusScanner(Substitute.For<ILogger<AllowWithLogVirusScanner>>()),
        new LogOnlyRecruitmentNotificationService(Substitute.For<ILogger<LogOnlyRecruitmentNotificationService>>()),
        new GanssHtmlSanitizer(),
        Substitute.For<ILogger<ApplicantService>>());

    private ICurrentUser AnonymousUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.IsAuthenticated.Returns(false);       // public careers-page submission
        u.UserId.Returns(Guid.Empty);
        return u;
    }

    private ICurrentUser AuthenticatedUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.IsAuthenticated.Returns(true);
        u.UserId.Returns(_userId);
        u.Email.Returns("recruiter@acme.com");
        return u;
    }

    private void SeedTenant(bool publicCareersEnabled)
    {
        using var db = Db();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Subdomain = "acme",
            Name = "Acme Corp",
            Status = TenantStatus.Active,
            PublicCareersEnabled = publicCareersEnabled,
        });
        db.SaveChanges();
    }

    private void SeedOpenVacancy()
    {
        using var db = Db();
        db.Vacancies.Add(new Vacancy
        {
            Id = _vacancyId,
            TenantId = _tenantId,
            ReferenceNumber = "VAC-2026-0001",
            Title = "Backend Engineer",
            Status = VacancyStatus.Open,
            EmploymentType = EmploymentType.FullTime,
            Headcount = 1,
            Description = "Build things.",
            PublishToPublicCareers = true,
            PublishedAt = DateTime.UtcNow,
            IsDeleted = false,
        });
        db.SaveChanges();
    }

    private Guid SeedEmployee()
    {
        using var db = Db();
        var id = Guid.NewGuid();
        db.Employees.Add(new Employee
        {
            Id = id, TenantId = _tenantId, UserId = _userId,
            EmployeeNo = "EMP-0001", FirstName = "Intern", LastName = "Person",
            Email = "intern@acme.com", Status = EmployeeStatus.Active, IsDeleted = false,
        });
        db.SaveChanges();
        return id;
    }

    private static SubmitApplicationInput Apply(
        Guid vacancyId, string email, ApplicationSource source = ApplicationSource.Public,
        Guid? linkedEmployeeId = null)
        => new(
            vacancyId, "Ada", "Lovelace", email, "+1-555-0100", "Cover letter.",
            new MemoryStream(HRM.Tests.Unit.Helpers.UploadTestBytes.Pdf), "resume.pdf", "application/pdf", HRM.Tests.Unit.Helpers.UploadTestBytes.Pdf.Length,
            source, linkedEmployeeId);

    // ── Public apply, careers DISABLED → rejected with 404 vacancy_not_found ──

    [Fact]
    public async Task PublicApply_CareersDisabled_Returns404_ISSUE095()
    {
        SeedTenant(publicCareersEnabled: false);
        SeedOpenVacancy();

        var result = await Service(AnonymousUser()).SubmitAsync(Apply(_vacancyId, "ada@example.com"));

        // Pre-fix: no toggle check → falls through to the open vacancy → succeeds → this FAILS.
        // Post-fix: the disabled toggle short-circuits with a 404 before any vacancy disclosure.
        result.IsFailure.Should().BeTrue("the public path is closed when PublicCareersEnabled is off");
        result.StatusCode.Should().Be(404);
        result.ErrorCode.Should().Be("vacancy_not_found");
    }

    // ── Public apply, careers ENABLED → NOT blocked by the toggle (reaches normal
    //    processing). Same seeded vacancy as above, so the toggle is the only variable. ──

    [Fact]
    public async Task PublicApply_CareersEnabled_NotBlockedByToggle_ISSUE095()
    {
        SeedTenant(publicCareersEnabled: true);
        SeedOpenVacancy();

        var result = await Service(AnonymousUser()).SubmitAsync(Apply(_vacancyId, "ada@example.com"));

        // With the toggle on and the vacancy present/open, the submission proceeds to a
        // successful application — it is NOT rejected by the toggle 404.
        result.IsSuccess.Should().BeTrue(
            "an enabled tenant with an open vacancy must not be blocked by the careers toggle");
    }

    // ── Internal (authenticated) apply is unaffected by the toggle even when OFF ──

    [Fact]
    public async Task InternalApply_CareersDisabled_NotBlockedByToggle_ISSUE095()
    {
        SeedTenant(publicCareersEnabled: false);
        SeedOpenVacancy();
        var employeeId = SeedEmployee();

        var result = await Service(AuthenticatedUser()).SubmitAsync(
            Apply(_vacancyId, "intern@acme.com", ApplicationSource.Internal, employeeId));

        // The toggle guards ONLY ApplicationSource.Public — the internal path succeeds
        // despite PublicCareersEnabled being off.
        result.IsSuccess.Should().BeTrue("the toggle must not affect the internal apply path");
    }

    // In-memory IFileStorage stub — avoids the filesystem while still exercising the
    // virus-scan-then-store ordering in ApplicantService.SubmitAsync.
    private sealed class InMemoryFileStorage : IFileStorage
    {
        public Task<string> UploadAsync(Guid tenantId, string relativePath, Stream content, string contentType, CancellationToken cancellationToken = default)
            => Task.FromResult($"/{tenantId}/{relativePath}");
        public Task<Stream?> OpenReadAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]));
        public string GetSignedUrl(Guid tenantId, string relativePath, TimeSpan? expiresIn = null)
            => $"/files/{tenantId}/{relativePath}";
        public Task DeleteAsync(Guid tenantId, string relativePath, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
