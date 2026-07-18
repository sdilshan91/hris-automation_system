// ============================================================================
// ISSUE-104 (US-REC-005) — Applicant submission MUST write an audit_logs row.
//
// Regression tests for the "missing-audit-write" cluster. ApplicantService.SubmitAsync
// persists a new Applicant at stage Applied but (pre-fix) writes NO audit row for the
// submission itself — only later stage-moves are audited. The fix adds an
// "Application.Submitted" audit_logs row (LeaveTypeService.AddLeaveTypeAudit pattern):
// tenant stamped from context/vacancy, ResourceId == the applicant, actor = the user
// when authenticated and NULL on the public/anonymous submission path.
//
// These tests drive the REAL ApplicantService (same InMemory harness as
// ApplicantSubmissionIntegrationTests) and assert the audit row is present. They FAIL
// pre-fix (no row) and PASS post-fix. Audit rows are plain inserts, so InMemory is a
// faithful check of row PRESENCE + action + resourceId (+ actor).
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

public sealed class ApplicantSubmissionAuditTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly ITenantContext _tenantContext;
    private readonly Guid _vacancyId = Guid.NewGuid();

    public ApplicantSubmissionAuditTests()
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

    private void SeedOpenVacancy()
    {
        using var db = Db();
        // ISSUE-095: public apply now requires the tenant's PublicCareersEnabled toggle ON.
        // Seed the tenant with it enabled so the public submission path is not 404-gated.
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId,
            Subdomain = "acme",
            Name = "Acme Corp",
            Status = TenantStatus.Active,
            PublicCareersEnabled = true,
        });
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

    private static bool ActionContains(AuditLog a, string s)
        => (a.Action?.Contains(s) ?? false) || (a.EventType?.Contains(s) ?? false);

    // ── ISSUE-103: applicant free-text (name, cover letter) must be server-side sanitized ──
    // The sibling vacancy Description path already runs the HTML sanitizer; the applicant path did not,
    // so an injected <script>/onerror payload was stored verbatim (stored-XSS). The fix applies the same
    // GanssHtmlSanitizer to FirstName/LastName/CoverLetter on write.

    private static SubmitApplicationInput ApplyRaw(
        Guid vacancyId, string firstName, string lastName, string? coverLetter)
        => new(
            vacancyId, firstName, lastName, "xss@example.com", "+1-555-0100", coverLetter,
            new MemoryStream(HRM.Tests.Unit.Helpers.UploadTestBytes.Pdf), "resume.pdf", "application/pdf",
            HRM.Tests.Unit.Helpers.UploadTestBytes.Pdf.Length, ApplicationSource.Public, null);

    [Fact]
    public async Task SubmitApplication_StripsScriptFromName_And_CoverLetter_ISSUE103()
    {
        SeedOpenVacancy();

        var result = await Service(AnonymousUser()).SubmitAsync(ApplyRaw(
            _vacancyId,
            firstName: "Ada<script>alert(1)</script>",
            lastName: "Lovelace",
            coverLetter: "Hello<img src=x onerror=alert(1)> <script>steal()</script>world"));
        result.IsSuccess.Should().BeTrue();

        using var db = Db();
        var applicant = await db.Applicants.AsNoTracking()
            .SingleAsync(a => a.Email == "xss@example.com");

        // The script/handler payloads are stripped on write; the safe text survives.
        applicant.FirstName.Should().NotContain("script");
        applicant.FirstName.Should().NotContain("alert");
        applicant.FirstName.Should().Contain("Ada");

        applicant.CoverLetter.Should().NotContain("script");
        applicant.CoverLetter.Should().NotContain("onerror");
        applicant.CoverLetter.Should().NotContain("steal");
        applicant.CoverLetter.Should().Contain("Hello");
        applicant.CoverLetter.Should().Contain("world");
    }

    // ── Public/anonymous submission: audit row present, tenant-scoped, actor NULL ──

    [Fact]
    public async Task SubmitApplication_Public_WritesAuditRow_ISSUE104()
    {
        SeedOpenVacancy();

        var result = await Service(AnonymousUser()).SubmitAsync(Apply(_vacancyId, "ada@example.com"));
        result.IsSuccess.Should().BeTrue();

        using var db = Db();
        var applicant = await db.Applicants.AsNoTracking()
            .SingleAsync(a => a.Email == "ada@example.com");

        var audits = await db.AuditLogs.AsNoTracking()
            .Where(a => a.ResourceId == applicant.Id.ToString())
            .ToListAsync();

        // Pre-fix: no submission audit row exists -> this FAILS. Post-fix: exactly the row below.
        audits.Should().ContainSingle(a => ActionContains(a, "Submitted"),
            "the submission must write an Application.Submitted audit row (ISSUE-104)");

        var row = audits.Single(a => ActionContains(a, "Submitted"));
        ActionContains(row, "Application").Should().BeTrue();
        row.ResourceId.Should().Be(applicant.Id.ToString());
        row.TenantId.Should().Be(_tenantId);           // tenant from the resolved/vacancy tenant
        row.UserId.Should().BeNull();                  // public path: actor may be (and here is) null
    }

    // ── Internal (authenticated) submission: same audit row, actor = the user ──

    [Fact]
    public async Task SubmitApplication_Internal_WritesAuditRow_WithActor_ISSUE104()
    {
        SeedOpenVacancy();
        var employeeId = SeedEmployee();

        var result = await Service(AuthenticatedUser()).SubmitAsync(
            Apply(_vacancyId, "intern@acme.com", ApplicationSource.Internal, employeeId));
        result.IsSuccess.Should().BeTrue();

        using var db = Db();
        var applicant = await db.Applicants.AsNoTracking()
            .SingleAsync(a => a.Email == "intern@acme.com");

        var row = await db.AuditLogs.AsNoTracking()
            .SingleOrDefaultAsync(a => a.ResourceId == applicant.Id.ToString()
                && (a.Action!.Contains("Submitted") || a.EventType.Contains("Submitted")));

        row.Should().NotBeNull("an authenticated internal submission must also be audited (ISSUE-104)");
        row!.TenantId.Should().Be(_tenantId);
        row.UserId.Should().Be(_userId);               // authenticated path stamps the actor
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
