// ============================================================================
// US-REC-009: Recruitment dashboard + analytics — integration tests.
//
// Exercises the full handler -> service -> DbContext path through a real DI container (MediatR
// pipeline + ITenantContext-driven global query filters), covering:
//   - KPIs: open vacancies, total applicants (period), hires (period), average time-to-hire (BR-1),
//     offer acceptance rate 7/10 = 70% (BR-2), offers awaiting response (AC-1/FR-1).
//   - Funnel: 100 Applied / 60 Screening / 30 Interview / 10 Offer / 5 Hired with adjacent conversions
//     60% / 50% / 33.33% / 50% (AC-3/FR-2/BR-3).
//   - Source effectiveness: applicants + hire conversion per source (AC-4/FR-3/BR-6).
//   - Vacancy status summary (FR-5) and recent-activity feed cap (FR-9).
//   - Date-range filter narrows the period metrics (AC-2/FR-6).
//   - CSV + XLSX export are non-empty (FR-8).
//   - Multi-tenant isolation: Tenant B sees zero of Tenant A (AC-5 via EF global filters).
//
// NOTE ON PROVIDER: identical rationale to the other dashboard integration tests — the verify gate runs
// `dotnet test` with NO PostgreSQL bound and Docker is unavailable in the agent sandbox, so a
// Testcontainers test would red the gate. These tests use the InMemory provider through the real composed
// pipeline (MediatR + tenant query filters), which is what proves tenant isolation.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Recruitment.DTOs;
using HRM.Application.Features.Recruitment.Queries;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HRM.Tests.Integration;

public sealed class RecruitmentDashboardIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _deptEng = Guid.NewGuid();
    private readonly Guid _vacancyA = Guid.NewGuid();
    private readonly Guid _vacancyB = Guid.NewGuid();

    // ── ISSUE-137 (BR-5) department-scope fixture: a dedicated tenant with two departments so the existing
    //    _tenantA / _tenantB seeds (and every assertion against them) stay untouched. _deptEng gets 3
    //    applicants, _deptOther gets 5; the dept-scoped caller is an employee in _deptEng. ──
    private readonly Guid _tenantScoped = Guid.NewGuid();
    private readonly Guid _deptOther = Guid.NewGuid();
    private readonly Guid _scopedEngVacancy = Guid.NewGuid();
    private readonly Guid _scopedOtherVacancy = Guid.NewGuid();
    private readonly Guid _scopedManagerUserId = Guid.NewGuid();
    private const int ScopedEngApplicants = 3;
    private const int ScopedOtherApplicants = 5;

    // The reporting period: April 2026 (a fully-completed month in the past).
    private static readonly DateOnly From = new(2026, 4, 1);
    private static readonly DateOnly To = new(2026, 4, 30);
    private static readonly DateTime AppliedAtBase = new(2026, 4, 2, 9, 0, 0, DateTimeKind.Utc);

    public RecruitmentDashboardIntegrationTests()
    {
        Seed();
        SeedScopedTenant();
    }

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

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; init; }
        public string Email => "hr@t.com";
        public Guid TenantId { get; init; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions { get; init; } = [];
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
    }

    private IMediator BuildPipeline(
        Guid tenantId, IReadOnlyList<string>? permissions = null, Guid? userId = null)
    {
        var tenantContext = new MutableTenantContext { TenantId = tenantId };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<ICurrentUser>(new FakeCurrentUser
        {
            UserId = userId ?? Guid.NewGuid(),
            TenantId = tenantId,
            Permissions = permissions ?? new[] { PermissionCatalog.Recruitment.View },
        });
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        services.AddScoped<IRecruitmentDashboardService, RecruitmentDashboardService>();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(RecruitmentDashboardQuery).Assembly));

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IMediator>();
    }

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private static RecruitmentDashboardFilter PeriodFilter(Guid? vacancyId = null, Guid? departmentId = null)
        => new() { From = From, To = To, VacancyId = vacancyId, DepartmentId = departmentId };

    // ── Seed: Tenant A gets the 100/60/30/10/5 funnel + offers; Tenant B gets a separate vacancy. ──
    private void Seed()
    {
        using var db = Db(_tenantA);

        db.Vacancies.Add(new Vacancy
        {
            Id = _vacancyA, TenantId = _tenantA, ReferenceNumber = "VAC-2026-0001",
            Title = "Engineer", Status = VacancyStatus.Open, DepartmentId = _deptEng,
            Headcount = 5, Description = "<p>x</p>",
        });
        // A second Tenant-A vacancy in Draft so the status summary has variety.
        db.Vacancies.Add(new Vacancy
        {
            Id = Guid.NewGuid(), TenantId = _tenantA, ReferenceNumber = "VAC-2026-0002",
            Title = "Analyst", Status = VacancyStatus.Draft, DepartmentId = _deptEng,
            Headcount = 1, Description = "<p>y</p>",
        });

        // Tenant B vacancy (must never appear in Tenant A's dashboard).
        db.Vacancies.Add(new Vacancy
        {
            Id = _vacancyB, TenantId = _tenantB, ReferenceNumber = "VAC-2026-0001",
            Title = "Other", Status = VacancyStatus.Open, DepartmentId = Guid.NewGuid(),
            Headcount = 1, Description = "<p>z</p>",
        });

        // 100 applicants. Reach distribution (cumulative): 100 Applied, 60 Screening, 30 Interview,
        // 10 Offer, 5 Hired. We set each applicant's CURRENT stage to the highest stage they reached so
        // the funnel "reached" logic (current-stage rank OR history) credits every lower stage too.
        //   idx 0..4   -> Hired (5)
        //   idx 5..9   -> Offer (5)   => 10 reached Offer
        //   idx 10..29 -> Interview (20) => 30 reached Interview
        //   idx 30..59 -> Screening (30) => 60 reached Screening
        //   idx 60..99 -> Applied (40) => 100 reached Applied
        var applicantIds = new List<Guid>();
        for (int i = 0; i < 100; i++)
        {
            ApplicantStage stage = i switch
            {
                < 5 => ApplicantStage.Hired,
                < 10 => ApplicantStage.Offer,
                < 30 => ApplicantStage.Interview,
                < 60 => ApplicantStage.Screening,
                _ => ApplicantStage.Applied,
            };

            // Sources: first 50 Public, next 30 Internal, last 20 Referral.
            ApplicationSource source = i < 50 ? ApplicationSource.Public
                : i < 80 ? ApplicationSource.Internal
                : ApplicationSource.Referral;

            var id = Guid.NewGuid();
            applicantIds.Add(id);
            db.Applicants.Add(new Applicant
            {
                Id = id, TenantId = _tenantA, VacancyId = _vacancyA,
                ApplicationReferenceNumber = $"APP-2026-{i:D4}",
                FirstName = $"App{i}", LastName = "Test", Email = $"app{i}@t.com",
                ResumeStorageKey = "k", ResumeFileName = "cv.pdf",
                Stage = stage, Source = source, AppliedAt = AppliedAtBase,
            });

            // Stage history: write a row for every stage the applicant passed through (Applied->...).
            int reachedRank = stage switch
            {
                ApplicantStage.Hired => 4,
                ApplicantStage.Offer => 3,
                ApplicantStage.Interview => 2,
                ApplicantStage.Screening => 1,
                _ => 0,
            };
            var order = new[]
            {
                ApplicantStage.Applied, ApplicantStage.Screening, ApplicantStage.Interview,
                ApplicantStage.Offer, ApplicantStage.Hired,
            };
            for (int s = 1; s <= reachedRank; s++)
            {
                db.ApplicantStageHistories.Add(new ApplicantStageHistory
                {
                    Id = BaseEntity.NewUuidV7(), TenantId = _tenantA, ApplicantId = id,
                    FromStage = order[s - 1], ToStage = order[s],
                    // Hired entered on day 12 of the period (applied day 2 -> 10 days time-to-hire).
                    ChangedAt = order[s] == ApplicantStage.Hired
                        ? new DateTime(2026, 4, 12, 9, 0, 0, DateTimeKind.Utc)
                        : new DateTime(2026, 4, 5, 9, 0, 0, DateTimeKind.Utc),
                });
            }
        }

        // 10 offers SENT in the period; 7 Accepted, 3 awaiting response -> acceptance 70%, awaiting 3.
        for (int i = 0; i < 10; i++)
        {
            db.Offers.Add(new Offer
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantA,
                ApplicantId = applicantIds[i], VacancyId = _vacancyA,
                OfferReferenceNumber = $"OFR-2026-{i:D4}",
                Status = i < 7 ? OfferStatus.Accepted : OfferStatus.Sent,
                OfferedPosition = "Engineer", SalaryAmount = 1000, Currency = "USD",
                StartDate = new DateOnly(2026, 5, 1), ExpiryDate = new DateOnly(2026, 5, 8),
                SentAt = new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc),
                RespondedAt = i < 7 ? new DateTime(2026, 4, 11, 9, 0, 0, DateTimeKind.Utc) : null,
                Response = i < 7 ? "Accepted" : null,
            });
        }

        // Tenant B: one applicant + one hire, to prove isolation.
        var bApplicant = Guid.NewGuid();
        db.Applicants.Add(new Applicant
        {
            Id = bApplicant, TenantId = _tenantB, VacancyId = _vacancyB,
            ApplicationReferenceNumber = "APP-2026-0001",
            FirstName = "BApp", LastName = "X", Email = "b@t.com",
            ResumeStorageKey = "k", ResumeFileName = "cv.pdf",
            Stage = ApplicantStage.Hired, Source = ApplicationSource.Public, AppliedAt = AppliedAtBase,
        });
        db.ApplicantStageHistories.Add(new ApplicantStageHistory
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantB, ApplicantId = bApplicant,
            FromStage = ApplicantStage.Offer, ToStage = ApplicantStage.Hired,
            ChangedAt = new DateTime(2026, 4, 12, 9, 0, 0, DateTimeKind.Utc),
        });

        db.SaveChanges();
    }

    private async Task<RecruitmentDashboardDto> GetDashboard(Guid tenant, RecruitmentDashboardFilter filter)
    {
        var mediator = BuildPipeline(tenant);
        var result = await mediator.Send(new RecruitmentDashboardQuery(filter));
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    // ── ISSUE-137 (BR-5) department-scope fixture + helpers ─────────────────────────────────────────

    /// <summary>
    /// Seeds <see cref="_tenantScoped"/> with two departments: <see cref="_deptEng"/> (3 applicants on an
    /// Open vacancy) and <see cref="_deptOther"/> (5 applicants on an Open vacancy), plus an employee in
    /// _deptEng whose UserId is <see cref="_scopedManagerUserId"/> (the dept-scoped caller). All applicants
    /// applied in-period so a period query counts them. Kept entirely separate from _tenantA/_tenantB.
    /// </summary>
    private void SeedScopedTenant()
    {
        using var db = Db(_tenantScoped);

        db.Vacancies.Add(new Vacancy
        {
            Id = _scopedEngVacancy, TenantId = _tenantScoped, ReferenceNumber = "VAC-2026-9001",
            Title = "Eng Role", Status = VacancyStatus.Open, DepartmentId = _deptEng,
            Headcount = 1, Description = "<p>eng</p>",
        });
        db.Vacancies.Add(new Vacancy
        {
            Id = _scopedOtherVacancy, TenantId = _tenantScoped, ReferenceNumber = "VAC-2026-9002",
            Title = "Other Role", Status = VacancyStatus.Open, DepartmentId = _deptOther,
            Headcount = 1, Description = "<p>other</p>",
        });

        // The dept-scoped caller: an employee in _deptEng linked to _scopedManagerUserId.
        db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(), TenantId = _tenantScoped, UserId = _scopedManagerUserId,
            EmployeeNo = "EMP-9001", FirstName = "Dept", LastName = "Manager",
            Email = "deptmgr@t.com", DepartmentId = _deptEng,
        });

        for (int i = 0; i < ScopedEngApplicants; i++)
            db.Applicants.Add(ScopedApplicant(_scopedEngVacancy, $"ENG-{i:D4}", i));
        for (int i = 0; i < ScopedOtherApplicants; i++)
            db.Applicants.Add(ScopedApplicant(_scopedOtherVacancy, $"OTH-{i:D4}", i));

        db.SaveChanges();
    }

    private Applicant ScopedApplicant(Guid vacancyId, string suffix, int i) => new()
    {
        Id = Guid.NewGuid(), TenantId = _tenantScoped, VacancyId = vacancyId,
        ApplicationReferenceNumber = $"APP-2026-{suffix}",
        FirstName = $"S{i}", LastName = "Scoped", Email = $"s{suffix}@t.com",
        ResumeStorageKey = "k", ResumeFileName = "cv.pdf",
        Stage = ApplicantStage.Applied, Source = ApplicationSource.Public, AppliedAt = AppliedAtBase,
    };

    private async Task<RecruitmentDashboardDto> GetDashboardAs(
        Guid tenant, RecruitmentDashboardFilter filter, IReadOnlyList<string> permissions, Guid? userId)
    {
        var mediator = BuildPipeline(tenant, permissions, userId);
        var result = await mediator.Send(new RecruitmentDashboardQuery(filter));
        result.IsSuccess.Should().BeTrue(result.Error);
        return result.Value!;
    }

    // ══════════════════════════════════════════════════════════════
    //  KPIs (AC-1/FR-1, BR-1/BR-2)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Kpis_reflect_seeded_recruitment_data()
    {
        var d = await GetDashboard(_tenantA, PeriodFilter());

        d.Kpis.OpenVacancies.Should().Be(1);            // one Open vacancy in Tenant A
        d.Kpis.TotalApplicants.Should().Be(100);
        d.Kpis.Hires.Should().Be(5);                    // entered Hired within the period
        d.Kpis.AverageTimeToHireDays.Should().Be(10m);  // applied Apr 2 -> Hired Apr 12 (BR-1)
        d.Kpis.OfferAcceptanceRate.Should().Be(70m);    // 7/10 sent in period (BR-2)
        d.Kpis.OffersPending.Should().Be(3);            // status Sent, awaiting response
    }

    // ══════════════════════════════════════════════════════════════
    //  Funnel (AC-3/FR-2/BR-3)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Funnel_counts_and_conversions_match_the_seeded_distribution()
    {
        var d = await GetDashboard(_tenantA, PeriodFilter());

        var byStage = d.Funnel.ToDictionary(f => f.Stage, f => f);

        byStage["Applied"].Count.Should().Be(100);
        byStage["Screening"].Count.Should().Be(60);
        byStage["Interview"].Count.Should().Be(30);
        byStage["Offer"].Count.Should().Be(10);
        byStage["Hired"].Count.Should().Be(5);

        byStage["Applied"].ConversionRate.Should().BeNull();
        byStage["Screening"].ConversionRate.Should().Be(60m);   // 60/100
        byStage["Interview"].ConversionRate.Should().Be(50m);   // 30/60
        byStage["Offer"].ConversionRate.Should().Be(33.33m);    // 10/30
        byStage["Hired"].ConversionRate.Should().Be(50m);       // 5/10
    }

    // ══════════════════════════════════════════════════════════════
    //  Source effectiveness (AC-4/FR-3/BR-6)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Source_effectiveness_counts_applicants_and_hires_per_source()
    {
        var d = await GetDashboard(_tenantA, PeriodFilter());

        var bySource = d.Sources.ToDictionary(s => s.Source, s => s);

        // 50 Public, 30 Internal, 20 Referral applicants.
        bySource["Public"].ApplicantCount.Should().Be(50);
        bySource["Internal"].ApplicantCount.Should().Be(30);
        bySource["Referral"].ApplicantCount.Should().Be(20);

        // The 5 hires are idx 0..4, all in the first 50 (Public).
        bySource["Public"].HireCount.Should().Be(5);
        bySource["Internal"].HireCount.Should().Be(0);
        bySource["Public"].ConversionRate.Should().Be(10m);   // 5/50
    }

    // ══════════════════════════════════════════════════════════════
    //  Vacancy status summary (FR-5) + recent activity (FR-9)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Vacancy_status_summary_counts_each_status()
    {
        var d = await GetDashboard(_tenantA, PeriodFilter());

        var byStatus = d.VacancyStatusSummary.ToDictionary(v => v.Status, v => v.Count);
        byStatus["Open"].Should().Be(1);
        byStatus["Draft"].Should().Be(1);
        byStatus["Closed"].Should().Be(0);
    }

    [Fact]
    public async Task Recent_activity_is_capped_and_newest_first()
    {
        var d = await GetDashboard(_tenantA, PeriodFilter());

        d.RecentActivity.Should().HaveCountLessThanOrEqualTo(20);
        d.RecentActivity.Should().BeInDescendingOrder(e => e.OccurredAt);
    }

    // ══════════════════════════════════════════════════════════════
    //  Date-range filter (AC-2/FR-6)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Date_range_filter_excludes_applicants_outside_the_period()
    {
        // A window entirely before the applications (all applied Apr 2) -> zero period applicants.
        var narrow = new RecruitmentDashboardFilter
        {
            From = new DateOnly(2026, 1, 1),
            To = new DateOnly(2026, 1, 31),
        };

        var d = await GetDashboard(_tenantA, narrow);

        d.Kpis.TotalApplicants.Should().Be(0);
        d.Kpis.Hires.Should().Be(0);
        d.Funnel.Single(f => f.Stage == "Applied").Count.Should().Be(0);
    }

    // ══════════════════════════════════════════════════════════════
    //  Multi-tenant isolation (AC-5)
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task Tenant_B_sees_only_its_own_data()
    {
        var d = await GetDashboard(_tenantB, PeriodFilter());

        d.Kpis.TotalApplicants.Should().Be(1);   // not Tenant A's 100
        d.Kpis.Hires.Should().Be(1);
        d.Kpis.OpenVacancies.Should().Be(1);
        d.Kpis.OffersPending.Should().Be(0);     // Tenant A's offers are invisible
        d.Funnel.Single(f => f.Stage == "Applied").Count.Should().Be(1);
    }

    [Fact]
    public async Task Tenant_A_dashboard_excludes_tenant_B()
    {
        var d = await GetDashboard(_tenantA, PeriodFilter());

        // 100 of A's applicants, none of B's; B's single applicant must not bleed in.
        d.Kpis.TotalApplicants.Should().Be(100);
    }

    // ══════════════════════════════════════════════════════════════
    //  Export (FR-8)
    // ══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public async Task Export_produces_a_non_empty_file(string format)
    {
        var mediator = BuildPipeline(_tenantA);
        var result = await mediator.Send(new ExportRecruitmentDashboardQuery(PeriodFilter(), format));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.FileContent.Should().NotBeEmpty();
        result.Value!.FileName.Should().Contain("recruitment-dashboard");
    }

    [Fact]
    public async Task Export_rejects_unknown_format()
    {
        var mediator = BuildPipeline(_tenantA);
        var result = await mediator.Send(new ExportRecruitmentDashboardQuery(PeriodFilter(), "png"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_format");
    }

    // ══════════════════════════════════════════════════════════════
    //  ISSUE-137 (BR-5): department-scoped recruitment dashboard
    // ══════════════════════════════════════════════════════════════

    private static string[] DeptScopeOnly => new[] { PermissionCatalog.Reports.ViewDepartment };

    [Fact]
    public async Task DeptScoped_caller_sees_only_own_department_metrics_ISSUE137()
    {
        // Caller holds ONLY Reports.View.Department and is an employee in _deptEng. The dashboard must
        // auto-restrict to _deptEng's 3 applicants and exclude _deptOther's 5.
        var d = await GetDashboardAs(_tenantScoped, PeriodFilter(), DeptScopeOnly, _scopedManagerUserId);

        d.Kpis.TotalApplicants.Should().Be(ScopedEngApplicants);
        d.Funnel.Single(f => f.Stage == "Applied").Count.Should().Be(ScopedEngApplicants);
        d.Kpis.OpenVacancies.Should().Be(1); // only _deptEng's Open vacancy is in scope, not _deptOther's
    }

    [Fact]
    public async Task DeptScoped_caller_cannot_widen_via_departmentId_param_ISSUE137()
    {
        // Same dept-scoped caller, but supplies the OTHER department's id — the param must NOT widen scope.
        var filter = PeriodFilter(departmentId: _deptOther);
        var d = await GetDashboardAs(_tenantScoped, filter, DeptScopeOnly, _scopedManagerUserId);

        // Still clamped to their OWN department (_deptEng = 3), NOT _deptOther (5).
        d.Kpis.TotalApplicants.Should().Be(ScopedEngApplicants);
        d.Kpis.TotalApplicants.Should().NotBe(ScopedOtherApplicants);
        d.Kpis.OpenVacancies.Should().Be(1); // and the vacancy count doesn't leak _deptOther's Open vacancy either.
    }

    [Fact]
    public async Task DeptScoped_caller_cannot_widen_via_vacancyId_param_ISSUE137()
    {
        // Second anti-widening vector: the dept-scoped caller (in _deptEng) supplies a VacancyId that belongs to
        // _deptOther. The own-department clamp INTERSECTS the supplied vacancy with the caller's own-dept set, so
        // an out-of-department vacancy resolves to NOTHING — the caller cannot reach _deptOther's data via VacancyId.
        var filter = PeriodFilter(vacancyId: _scopedOtherVacancy);
        var d = await GetDashboardAs(_tenantScoped, filter, DeptScopeOnly, _scopedManagerUserId);

        d.Kpis.TotalApplicants.Should().Be(0);   // _scopedOtherVacancy is outside _deptEng → empty scope, not _deptOther's 5.
        d.Kpis.OpenVacancies.Should().Be(0);
        d.Funnel.Single(f => f.Stage == "Applied").Count.Should().Be(0);
    }

    [Fact]
    public async Task DeptScoped_caller_with_no_employee_record_gets_empty_dashboard_failclosed_ISSUE137()
    {
        // Fail-closed: a Reports.View.Department caller whose user has NO employee row sees NOTHING —
        // never the whole tenant.
        var d = await GetDashboardAs(_tenantScoped, PeriodFilter(), DeptScopeOnly, Guid.NewGuid());

        d.Kpis.TotalApplicants.Should().Be(0);
        d.Kpis.OpenVacancies.Should().Be(0);
        d.Funnel.Single(f => f.Stage == "Applied").Count.Should().Be(0);
    }

    [Fact]
    public async Task FullAccess_caller_sees_whole_tenant_unchanged_ISSUE137()
    {
        // Recruitment.View (full access) sees BOTH departments (3 + 5 = 8).
        var full = await GetDashboardAs(
            _tenantScoped, PeriodFilter(), new[] { PermissionCatalog.Recruitment.View }, _scopedManagerUserId);
        full.Kpis.TotalApplicants.Should().Be(ScopedEngApplicants + ScopedOtherApplicants);

        // Reports.View.All is also full access.
        var viewAll = await GetDashboardAs(
            _tenantScoped, PeriodFilter(), new[] { PermissionCatalog.Reports.ViewAll }, _scopedManagerUserId);
        viewAll.Kpis.TotalApplicants.Should().Be(ScopedEngApplicants + ScopedOtherApplicants);
    }

    [Fact]
    public async Task FullAccess_caller_departmentId_param_filters_normally_ISSUE137()
    {
        // A full-access caller CAN drill into a specific department via the param (unchanged FR-7 behaviour).
        var d = await GetDashboardAs(
            _tenantScoped, PeriodFilter(departmentId: _deptOther),
            new[] { PermissionCatalog.Recruitment.View }, _scopedManagerUserId);

        d.Kpis.TotalApplicants.Should().Be(ScopedOtherApplicants);
    }

    [Fact]
    public async Task DeptScoped_caller_export_is_scoped_to_own_department_ISSUE137()
    {
        // The export must route through the same scope resolution — the dept-scoped caller's file reflects
        // only their _deptEng data (3 applicants), never the whole tenant (8).
        var mediator = BuildPipeline(_tenantScoped, DeptScopeOnly, _scopedManagerUserId);
        var result = await mediator.Send(new ExportRecruitmentDashboardQuery(PeriodFilter(), "csv"));

        result.IsSuccess.Should().BeTrue(result.Error);
        var csv = System.Text.Encoding.UTF8.GetString(result.Value!.FileContent);
        csv.Should().Contain($"Total Applicants,{ScopedEngApplicants}");
        csv.Should().NotContain($"Total Applicants,{ScopedEngApplicants + ScopedOtherApplicants}");
    }
}
