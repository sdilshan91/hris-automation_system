// ============================================================================
// ISSUE-231 / ENH-021: the enum-from-DB 500 class, verified on REAL Postgres.
//
// Enum columns are stored as their NAME in a varchar column (HasConversion). EF's default (strict) converter
// THROWS during materialization if a row holds a string outside the enum — and because it throws while
// iterating the result set, ONE bad row 500s the WHOLE list/board query. The fix routes these read-heavy
// status columns through TolerantEnumToStringConverter, which maps an unknown string to an Unknown sentinel
// (and logs) instead of throwing.
//
// This can ONLY be reproduced on real Postgres: the InMemory provider stores/reads enums through the same
// converter, so there is no way to plant a genuine out-of-enum STRING in the column without a raw SQL UPDATE
// against a real database. Docker + Testcontainers are available on this box.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Recruitment.DTOs;
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

public sealed class TolerantEnumReadPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class MutableTenantContext : ITenantContext
    {
        public Guid TenantId { get; set; }
        public string Subdomain => "test";
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

    private AppDbContext Db()
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
            {
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                n.EnableRetryOnFailure(maxRetryCount: 3);
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);
    }

    // ── ISSUE-231: a corrupt applicant enum string does not 500 the board materialization ──
    [Fact]
    [Trait("TC", "TC-REC-003-15")]
    public async Task ApplicantRead_ToleratesUnknownSourceAndStage_OnPostgres_Issue231()
    {
        Guid vacancyId = BaseEntity.NewUuidV7(), badSourceId = BaseEntity.NewUuidV7(), badStageId = BaseEntity.NewUuidV7();
        await using (var seed = Db())
        {
            seed.Vacancies.Add(new Vacancy
            {
                Id = vacancyId, TenantId = _tenantId, ReferenceNumber = "VAC-2026-0001",
                Title = "Engineer", Status = VacancyStatus.Open, EmploymentType = EmploymentType.FullTime,
                Headcount = 1, Description = "Build things.",
            });
            seed.Applicants.Add(NewApplicant(badSourceId, vacancyId, "bad-source@t.com"));
            seed.Applicants.Add(NewApplicant(badStageId, vacancyId, "bad-stage@t.com"));
            await seed.SaveChangesAsync();

            // Plant strings OUTSIDE the enums, exactly the ISSUE-231 scenario ('CareerSite' etc.).
            await seed.Database.ExecuteSqlRawAsync(
                "UPDATE applicant SET source = 'CareerSite' WHERE id = {0}", badSourceId);
            await seed.Database.ExecuteSqlRawAsync(
                "UPDATE applicant SET stage = 'LegacyPhoneScreen' WHERE id = {0}", badStageId);
        }

        // A fresh context materializes every applicant (the board loads all applicants on the vacancy) —
        // the strict converter would throw here on the first bad row; the tolerant one maps to Unknown.
        await using var read = Db();
        List<Applicant> applicants = null!;
        var act = async () => applicants = await read.Applicants.AsNoTracking()
            .Where(a => a.VacancyId == vacancyId).ToListAsync();

        await act.Should().NotThrowAsync("one corrupt row must not 500 the whole board");
        applicants.Should().HaveCount(2);
        applicants.Single(a => a.Id == badSourceId).Source.Should().Be(ApplicationSource.Unknown);
        applicants.Single(a => a.Id == badStageId).Stage.Should().Be(ApplicantStage.Unknown);
    }

    // ── ISSUE-231 end-to-end: the actual pipeline-board SERVICE returns 200 (not 500) with a corrupt row ──
    [Fact]
    [Trait("TC", "TC-REC-003-15")]
    public async Task PipelineBoardService_ReturnsBoard_NotThrows_WithCorruptRows_OnPostgres_Issue231()
    {
        Guid vacancyId = BaseEntity.NewUuidV7();
        Guid goodId = BaseEntity.NewUuidV7(), badSourceId = BaseEntity.NewUuidV7(), badStageId = BaseEntity.NewUuidV7();
        await using (var seed = Db())
        {
            seed.Vacancies.Add(new Vacancy
            {
                Id = vacancyId, TenantId = _tenantId, ReferenceNumber = "VAC-2026-0002",
                Title = "Engineer", Status = VacancyStatus.Open, EmploymentType = EmploymentType.FullTime,
                Headcount = 1, Description = "Build things.",
            });
            seed.Applicants.Add(NewApplicant(goodId, vacancyId, "good@t.com"));       // Applied, clean.
            seed.Applicants.Add(NewApplicant(badSourceId, vacancyId, "badsrc@t.com")); // Applied, corrupt source.
            seed.Applicants.Add(NewApplicant(badStageId, vacancyId, "badstage@t.com")); // corrupt stage.
            await seed.SaveChangesAsync();

            await seed.Database.ExecuteSqlRawAsync(
                "UPDATE applicant SET source = 'CareerSite' WHERE id = {0}", badSourceId);
            await seed.Database.ExecuteSqlRawAsync(
                "UPDATE applicant SET stage = 'LegacyPhoneScreen' WHERE id = {0}", badStageId);
        }

        await using var read = Db();
        var service = new ApplicantService(read,
            new MutableTenantContext { TenantId = _tenantId }, Substitute.For<ICurrentUser>(),
            Substitute.For<IFileStorage>(), Substitute.For<IVirusScanner>(),
            Substitute.For<IRecruitmentNotificationService>(), NullLogger<ApplicantService>.Instance);

        var result = await service.GetPipelineBoardAsync(vacancyId, new PipelineFilter());

        result.IsSuccess.Should().BeTrue("a corrupt applicant row must not 500 the board");
        var board = result.Value!;
        board.Stages.Select(s => s.Stage).Should().NotContain(ApplicantStage.Unknown); // sentinel is never a column.

        var applied = board.Stages.Single(s => s.Stage == ApplicantStage.Applied);
        applied.Applicants.Select(c => c.Id).Should().BeEquivalentTo(new[] { goodId, badSourceId });
        applied.Applicants.Single(c => c.Id == badSourceId).Source.Should().Be(ApplicationSource.Unknown);

        // The corrupt-STAGE card maps to the Unknown sentinel, which has no column, so it is not shown anywhere.
        board.Stages.SelectMany(s => s.Applicants).Select(c => c.Id).Should().NotContain(badStageId);
    }

    // ── ISSUE-231 (dashboard read path): a corrupt stage-history string does not 500 the whole dashboard ──
    [Fact]
    [Trait("TC", "TC-REC-009-14")]
    public async Task RecruitmentDashboardService_ReturnsDashboard_NotThrows_WithCorruptStageHistory_OnPostgres_Issue231()
    {
        Guid vacancyId = BaseEntity.NewUuidV7(), applicantId = BaseEntity.NewUuidV7(), historyId = BaseEntity.NewUuidV7();
        await using (var seed = Db())
        {
            seed.Vacancies.Add(new Vacancy
            {
                Id = vacancyId, TenantId = _tenantId, ReferenceNumber = "VAC-2026-0003",
                Title = "Engineer", Status = VacancyStatus.Open, EmploymentType = EmploymentType.FullTime,
                Headcount = 1, Description = "Build things.",
            });
            seed.Applicants.Add(NewApplicant(applicantId, vacancyId, "hist@t.com"));
            seed.ApplicantStageHistories.Add(new ApplicantStageHistory
            {
                Id = historyId, TenantId = _tenantId, ApplicantId = applicantId,
                FromStage = ApplicantStage.Applied, ToStage = ApplicantStage.Screening, ChangedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();

            // Plant a stage-history string outside the enum — the dashboard projects every history row.
            await seed.Database.ExecuteSqlRawAsync(
                "UPDATE applicant_stage_history SET to_stage = 'LegacyPhoneScreen' WHERE id = {0}", historyId);
        }

        await using var read = Db();
        var service = new RecruitmentDashboardService(read,
            new MutableTenantContext { TenantId = _tenantId }, Substitute.For<ICurrentUser>(),
            NullLogger<RecruitmentDashboardService>.Instance);

        var result = await service.GetDashboardAsync(new RecruitmentDashboardFilter());

        // The strict converter would throw while materializing the corrupt to_stage → whole dashboard 500s.
        result.IsSuccess.Should().BeTrue("a corrupt stage-history row must not 500 the recruitment dashboard");
    }

    // ── ENH-021: a corrupt payroll-run status string does not 500 the run list materialization ──
    [Fact]
    [Trait("TC", "TC-PAY-017")]
    public async Task PayrollRunRead_ToleratesUnknownStatus_OnPostgres_Enh021()
    {
        var runId = BaseEntity.NewUuidV7();
        await using (var seed = Db())
        {
            seed.PayrollRuns.Add(new PayrollRun
            {
                Id = runId, TenantId = _tenantId, PayMonth = 6, PayYear = 2026,
                Status = PayrollRunStatus.Finalized, InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
            });
            await seed.SaveChangesAsync();

            await seed.Database.ExecuteSqlRawAsync(
                "UPDATE payroll_run SET status = 'Draft' WHERE id = {0}", runId);
        }

        await using var read = Db();
        List<PayrollRun> runs = null!;
        var act = async () => runs = await read.PayrollRuns.AsNoTracking()
            .Where(r => r.PayYear == 2026).ToListAsync();

        await act.Should().NotThrowAsync("a corrupt status must not 500 the run list");
        runs.Single(r => r.Id == runId).Status.Should().Be(PayrollRunStatus.Unknown);
    }

    private Applicant NewApplicant(Guid id, Guid vacancyId, string email) => new()
    {
        Id = id, TenantId = _tenantId, VacancyId = vacancyId,
        ApplicationReferenceNumber = $"APP-2026-{Math.Abs(email.GetHashCode()) % 10000:D4}",
        FirstName = "A", LastName = "B", Email = email,
        ResumeStorageKey = $"recruitment/{vacancyId}/x/z.pdf", ResumeFileName = "resume.pdf",
        Stage = ApplicantStage.Applied, Source = ApplicationSource.Public, AppliedAt = DateTime.UtcNow,
    };
}
