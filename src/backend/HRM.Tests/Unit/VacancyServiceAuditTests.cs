// ============================================================================
// BUG-055 regression (HIGH, Recruitment, US-REC-001, FR-7 / AC-3):
// The vacancy lifecycle (create / update / publish / close) must write a
// QUERYABLE audit_log row per action — with before/after snapshots — not just
// an ILogger line. Pre-fix, VacancyService only emits Serilog entries, so the
// vacancy lifecycle is unauditable and the existence assertions below FAIL.
// The fix (a backend agent, in VacancyService.cs) writes one AuditLog per
// action with correct Action/ResourceType/ResourceId, tenant + actor
// attribution, and before/after JSON snapshots — making these PASS.
//
// Mirrors RoleServiceRbacAuditTests (BUG-041) and LeaveTypeServiceAuditTests
// (BUG-025): EF Core InMemory + NSubstitute, query AuditLogs by ResourceId with
// IgnoreQueryFilters(). Audit rows are plain inserts, so InMemory is sufficient
// (the AuditLog.Before/After columns exist on the entity).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using HRM.Tests.Unit.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace HRM.Tests.Unit;

public sealed class VacancyServiceAuditTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _actorUserId = Guid.NewGuid();
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IHtmlSanitizer _sanitizer;

    public VacancyServiceAuditTests()
    {
        _tenantContext = Substitute.For<ITenantContext>();
        _tenantContext.TenantId.Returns(_tenantId);
        _tenantContext.IsResolved.Returns(true);
        _tenantContext.IsSystemContext.Returns(false);

        _currentUser = Substitute.For<ICurrentUser>();
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.UserId.Returns(_actorUserId);
        _currentUser.Email.Returns("recruiter@acme.com");

        // Pass-through sanitizer: keep body text intact so before/after content is meaningful.
        _sanitizer = Substitute.For<IHtmlSanitizer>();
        _sanitizer.Sanitize(Arg.Any<string?>()).Returns(ci => ci.Arg<string?>());
    }

    private AppDbContext Db() => TestDbContextFactory.Create(_tenantContext, _dbName);

    private VacancyService Service() =>
        new(Db(), _tenantContext, _currentUser, _sanitizer, NullLogger<VacancyService>.Instance);

    private static VacancyInput MakeInput(
        string title = "Software Engineer",
        Guid? departmentId = null,
        Guid? jobTitleId = null,
        Guid? hiringManagerId = null,
        int headcount = 1,
        string description = "<p>Build things.</p>") =>
        new(
            Title: title,
            DepartmentId: departmentId,
            JobTitleId: jobTitleId,
            LocationId: null,
            HiringManagerId: hiringManagerId,
            EmploymentType: EmploymentType.FullTime,
            Headcount: headcount,
            SalaryMin: null,
            SalaryMax: null,
            SalaryCurrency: null,
            Description: description,
            Qualifications: null,
            ApplicationDeadline: null,
            PublishToPublicCareers: true);

    /// <summary>
    /// Inserts a vacancy directly (bypassing the service) so a test can set up a specific lifecycle
    /// state to mutate. TenantId is stamped explicitly because the unit-test AppDbContext is built
    /// without the TenantInterceptor.
    /// </summary>
    private async Task<Guid> SeedVacancyAsync(
        VacancyStatus status,
        string title,
        bool publishReady = false)
    {
        using var db = Db();
        var vacancy = new Vacancy
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantId,
            ReferenceNumber = $"VAC-2026-{Random.Shared.Next(1000, 9999)}",
            Title = title,
            Status = status,
            EmploymentType = EmploymentType.FullTime,
            Headcount = publishReady ? 2 : 1,
            Description = "<p>Existing description.</p>",
            IsDeleted = false,
            // BR-2 publish requirements — the publish path validates the vacancy's own fields, it does
            // NOT re-check that these FKs exist, so arbitrary GUIDs are fine for state setup.
            DepartmentId = publishReady ? Guid.NewGuid() : null,
            JobTitleId = publishReady ? Guid.NewGuid() : null,
            HiringManagerId = publishReady ? Guid.NewGuid() : null,
        };
        db.Vacancies.Add(vacancy);
        await db.SaveChangesAsync();
        return vacancy.Id;
    }

    /// <summary>
    /// Finds the audit row for a vacancy action. Matches the action VERB by substring against either
    /// Action or EventType (the fix mirrors EventType = Action), so it is not brittle to the exact
    /// "Vacancy.Created" vs "Vacancy.Published" naming.
    /// </summary>
    private static async Task<AuditLog?> FindVacancyAuditAsync(AppDbContext db, Guid vacancyId, string verb)
    {
        var rows = await db.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.ResourceId == vacancyId.ToString())
            .ToListAsync();

        return rows.FirstOrDefault(a =>
            ((a.Action ?? string.Empty) + "|" + (a.EventType ?? string.Empty))
                .Contains(verb, StringComparison.OrdinalIgnoreCase));
    }

    // ── TC-REC-001-03: create is audited (existence) ─────────────────────────

    [Fact]
    public async Task CreateVacancy_WritesAuditRow()
    {
        var result = await Service().CreateAsync(MakeInput("Backend Engineer"));
        result.IsSuccess.Should().BeTrue();
        var vacancyId = result.Value!.Id;

        using var db = Db();
        var audit = await FindVacancyAuditAsync(db, vacancyId, "Created");

        audit.Should().NotBeNull("vacancy creation must write a queryable audit_log row (BUG-055, FR-7)");
        audit!.TenantId.Should().Be(_tenantId, "the audit row must be tenant-scoped");
        audit.UserId.Should().Be(_actorUserId, "the audit row must be actor-attributed");
        audit.ResourceType.Should().Contain("Vacancy");
        audit.ResourceId.Should().Be(vacancyId.ToString());
    }

    // ── TC-REC-001-04 / TC-REC-001-06: update is audited WITH before/after ────

    [Fact]
    public async Task UpdateVacancy_WritesBeforeAfterAuditRow()
    {
        var vacancyId = await SeedVacancyAsync(VacancyStatus.Draft, "Original Engineer Title");

        var update = await Service().UpdateAsync(vacancyId, MakeInput("Updated Senior Engineer Title"));
        update.IsSuccess.Should().BeTrue();

        using var db = Db();
        var audit = await FindVacancyAuditAsync(db, vacancyId, "Updated");

        audit.Should().NotBeNull("vacancy update must write a queryable audit_log row (BUG-055, FR-7)");
        audit!.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorUserId);
        audit.ResourceType.Should().Contain("Vacancy");
        audit.ResourceId.Should().Be(vacancyId.ToString());

        // Before/after snapshots must reflect the actual change, not be null/empty.
        audit.Before.Should().NotBeNullOrWhiteSpace("the before snapshot must capture prior state (AC-3)");
        audit.After.Should().NotBeNullOrWhiteSpace("the after snapshot must capture new state (AC-3)");
        audit.Before.Should().NotBe(audit.After);
        audit.Before!.Should().Contain("Original Engineer Title", "before must hold the pre-edit title");
        audit.After!.Should().Contain("Updated Senior Engineer Title", "after must hold the post-edit title");
    }

    // ── TC-REC-001-06: publish is audited WITH Draft→Open before/after ────────

    [Fact]
    public async Task PublishVacancy_WritesAuditRow()
    {
        var vacancyId = await SeedVacancyAsync(VacancyStatus.Draft, "Publishable Role", publishReady: true);

        var publish = await Service().PublishAsync(vacancyId);
        publish.IsSuccess.Should().BeTrue(publish.Error);

        using var db = Db();
        var audit = await FindVacancyAuditAsync(db, vacancyId, "Publish");

        audit.Should().NotBeNull("vacancy publish must write a queryable audit_log row (BUG-055, FR-7)");
        audit!.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorUserId);
        audit.ResourceType.Should().Contain("Vacancy");
        audit.ResourceId.Should().Be(vacancyId.ToString());

        // Before/after must reflect the Draft → Open (published) status transition.
        audit.Before.Should().NotBeNullOrWhiteSpace();
        audit.After.Should().NotBeNullOrWhiteSpace();
        audit.Before.Should().NotBe(audit.After);
        audit.Before!.Should().Contain(VacancyStatus.Draft.ToString(), "before must reflect the Draft status");
        audit.After!.Should().Contain(VacancyStatus.Open.ToString(), "after must reflect the published (Open) status");
    }

    // ── TC-REC-001-06: close/status-change is audited WITH Open→Closed ────────

    [Fact]
    public async Task CloseVacancy_WritesAuditRow()
    {
        var vacancyId = await SeedVacancyAsync(VacancyStatus.Open, "Role To Close");

        var close = await Service().CloseAsync(vacancyId);
        close.IsSuccess.Should().BeTrue(close.Error);

        using var db = Db();
        var audit = await FindVacancyAuditAsync(db, vacancyId, "Closed");

        audit.Should().NotBeNull("vacancy close must write a queryable audit_log row (BUG-055, FR-7)");
        audit!.TenantId.Should().Be(_tenantId);
        audit.UserId.Should().Be(_actorUserId);
        audit.ResourceType.Should().Contain("Vacancy");
        audit.ResourceId.Should().Be(vacancyId.ToString());

        // Before/after must reflect the Open → Closed status transition.
        audit.Before.Should().NotBeNullOrWhiteSpace();
        audit.After.Should().NotBeNullOrWhiteSpace();
        audit.Before.Should().NotBe(audit.After);
        audit.Before!.Should().Contain(VacancyStatus.Open.ToString(), "before must reflect the Open status");
        audit.After!.Should().Contain(VacancyStatus.Closed.ToString(), "after must reflect the Closed status");
    }
}
