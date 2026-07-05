// ============================================================================
// ISSUE-109 (US-REC-003) — optimistic-concurrency regression for ApplicantService.MoveStageAsync.
//
// The fix mirrors EmployeeService.UpdateProfileAsync / GoalService.UpdateAsync: the move carries the
// client's read-time token (expectedRowVersion) and the service sets it as the EF OriginalValue of the
// xmin concurrency token (Applicant.RowVersion, mapped .IsRowVersion()). A STALE token then raises
// DbUpdateConcurrencyException, surfaced as 409 "concurrency_conflict".
//
// HARNESS = real Postgres via Testcontainers (NOT InMemory). This is deliberate and load-bearing: xmin
// is a Postgres system column. The EF InMemory provider fires a *spurious* concurrency conflict for the
// .IsRowVersion() shim on any store-value divergence — so an InMemory test would go green even if the fix
// (the OriginalValue wiring) were reverted, i.e. it could not tell pre-fix from post-fix. Only Postgres
// exercises the real xmin guard:
//   • pre-fix  — the service re-reads the fresh xmin, so the guarded UPDATE always matches and a stale
//                stage move silently wins (no 409). This test FAILS pre-fix.
//   • post-fix — OriginalValue = the client's stale token, so UPDATE ... WHERE xmin = stale matches 0
//                rows -> DbUpdateConcurrencyException -> 409. This test PASSES.
//
// Schema is built with EnsureCreatedAsync() (xmin is intrinsic to every Postgres table); MigrateAsync is
// intentionally avoided (mirrors GoalConcurrencyPostgresTests — an unrelated uncommitted model change
// trips PendingModelChangesWarning in the working tree).
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class ApplicantConcurrencyPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _vacancyId = Guid.NewGuid();
    private readonly Guid _applicantId = Guid.NewGuid();
    private readonly MutableTenantContext _tc = new();
    private ICurrentUser _cu = null!;

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "acme";
        public TenantStatus Status => TenantStatus.Active;
        public string? Plan => null;
        public IReadOnlyCollection<string> EnabledModules => [];
        public string? LogoUrl => null;
        public string? PrimaryColor => null;
        public bool IsSystemContext => false;
        public bool IsResolved => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId, string subdomain, TenantStatus status,
            string? plan = null, IReadOnlyCollection<string>? enabledModules = null,
            string? logoUrl = null, string? primaryColor = null) => TenantId = tenantId;
        public void SetSystemContext() { }
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _tc.TenantId = _tenantId;
        _cu = Substitute.For<ICurrentUser>();
        _cu.IsAuthenticated.Returns(true);
        _cu.UserId.Returns(_userId);
        _cu.Email.Returns("hr@acme.com");

        await using var db = Db();
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        db.Vacancies.Add(new Vacancy
        {
            Id = _vacancyId, TenantId = _tenantId, ReferenceNumber = "VAC-2026-0001",
            Title = "Backend Engineer", Status = VacancyStatus.Open, EmploymentType = EmploymentType.FullTime,
            Headcount = 1, Description = "Build things.", PublishToPublicCareers = true,
            PublishedAt = DateTime.UtcNow, IsDeleted = false,
        });
        db.Applicants.Add(new Applicant
        {
            Id = _applicantId, TenantId = _tenantId, VacancyId = _vacancyId,
            ApplicationReferenceNumber = "APP-2026-0001", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", ResumeStorageKey = $"recruitment/{_vacancyId}/x/z.pdf",
            ResumeFileName = "resume.pdf", Stage = ApplicantStage.Applied, Source = ApplicationSource.Public,
            AppliedAt = DateTime.UtcNow, IsDeleted = false,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(_tc), new AuditInterceptor(_cu))
            .Options, _tc);

    private ApplicantService Service(AppDbContext db) => new(db, _tc, _cu,
        Substitute.For<IFileStorage>(), Substitute.For<IVirusScanner>(),
        Substitute.For<IRecruitmentNotificationService>(), NullLogger<ApplicantService>.Instance);

    // Reads the applicant's current xmin concurrency token.
    private async Task<uint> ReadTokenAsync()
    {
        await using var db = Db();
        return (await db.Applicants.AsNoTracking().FirstAsync(a => a.Id == _applicantId)).RowVersion;
    }

    // ── Happy path: move with the CURRENT token succeeds ──────────────────

    [Fact]
    public async Task MoveStage_CurrentToken_Succeeds_ISSUE109()
    {
        var token = await ReadTokenAsync();

        await using var db = Db();
        var result = await Service(db).MoveStageAsync(
            _applicantId, ApplicantStage.Screening, reason: null, notes: null, expectedRowVersion: token);

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.ToStage.Should().Be(ApplicantStage.Screening);

        await using var verify = Db();
        (await verify.Applicants.AsNoTracking().FirstAsync(a => a.Id == _applicantId))
            .Stage.Should().Be(ApplicantStage.Screening);
    }

    // ── Conflict: a stale token is rejected with 409 ──────────────────────

    [Fact]
    public async Task MoveStage_StaleToken_Returns409_ISSUE109()
    {
        // Two "reads" hold the SAME token (e.g. two recruiters opened the applicant).
        var token = await ReadTokenAsync();

        // First mover commits with the token -> Applied -> Screening succeeds and bumps xmin.
        await using (var db1 = Db())
        {
            var first = await Service(db1).MoveStageAsync(
                _applicantId, ApplicantStage.Screening, reason: null, notes: null, expectedRowVersion: token);
            first.IsSuccess.Should().BeTrue(first.Error);
        }

        // Second mover submits with the now-STALE token, targeting a DIFFERENT stage (so the move passes
        // the business rules and the ONLY failure is the concurrency guard) -> 409, change NOT applied.
        await using (var db2 = Db())
        {
            var second = await Service(db2).MoveStageAsync(
                _applicantId, ApplicantStage.Interview, reason: null, notes: null, expectedRowVersion: token);
            second.IsFailure.Should().BeTrue("the stale token no longer matches the row's xmin");
            second.StatusCode.Should().Be(409);
            second.ErrorCode.Should().Be("concurrency_conflict");
        }

        // The first mover's change persisted; the stale move was rejected (no last-write-wins).
        await using var verify = Db();
        (await verify.Applicants.AsNoTracking().FirstAsync(a => a.Id == _applicantId))
            .Stage.Should().Be(ApplicantStage.Screening);
    }
}
