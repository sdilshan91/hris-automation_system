// ============================================================================
// US-PRF-011 — calibrated-rating model: real-Postgres tenant-isolation + numeric round-trip.
//
// WHY REAL POSTGRES (not InMemory): the rating_calibration numeric(6,2) columns and the tenant global query
// filter must be proven against a live store. This closes two gaps InMemory cannot: (1) the numeric scores
// serialize/deserialize on a real column (fresh-context read-back, not an in-memory object ref), and (2) a
// calibration written under tenant A is INVISIBLE to a tenant-B-scoped context via the EF global query filter
// (NFR-2 cross-tenant isolation) — the non-negotiable arm in this codebase.
//
// HARNESS = Testcontainers Postgres, EnsureCreatedAsync() for the schema (same rationale + pattern as
// AppraisalCycleScopePostgresTests: sidesteps PendingModelChangesWarning; the migration + its dormant RLS
// policy are exercised by the migrations CI job / RlsIsolationPostgresTests). Isolation here is the EF
// query-filter layer, matching the performance module's posture.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
using HRM.Domain.Authorization;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Performance;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Persistence.Interceptors;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace HRM.Tests.Integration;

public sealed class RatingCalibrationIsolationPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private readonly Guid _deptA = Guid.NewGuid();
    private readonly Guid _jobTitleA = Guid.NewGuid();
    private readonly Guid _empA = Guid.NewGuid();
    private readonly Guid _cycleA = Guid.NewGuid();
    private readonly Guid _reviewA = Guid.NewGuid();
    private readonly Guid _hrUserA = Guid.NewGuid();

    private readonly MutableTenantContext _tc = new();

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

        _tc.TenantId = _tenantA;
        await using var db = Db();
        await db.Database.EnsureCreatedAsync();

        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = "acme", Name = "Acme" });
        db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = "globex", Name = "Globex" });
        db.Departments.Add(new Department
        {
            Id = _deptA, TenantId = _tenantA, Name = "Engineering", Code = "ENG", IsActive = true, IsDeleted = false,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = _jobTitleA, TenantId = _tenantA, TitleName = "Engineer", IsActive = true, IsDeleted = false,
        });
        db.Employees.Add(new Employee
        {
            Id = _empA, TenantId = _tenantA, EmployeeNo = "ENG-1", FirstName = "Ada", LastName = "Lovelace",
            Email = "ada@acme.com", Status = EmployeeStatus.Active, IsActive = true,
            DepartmentId = _deptA, JobTitleId = _jobTitleA,
        });
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleA, TenantId = _tenantA, Name = "FY2026", Status = AppraisalCycleStatus.Active,
            StartDate = DateTime.UtcNow.AddDays(-30), EndDate = DateTime.UtcNow.AddDays(30),
            RatingScaleMax = 5, SelfWeightPercent = 30, IsCalibrationEnabled = true,
        });
        db.ManagerReviews.Add(new ManagerReview
        {
            Id = _reviewA, TenantId = _tenantA, CycleId = _cycleA, EmployeeId = _empA,
            Status = ManagerReviewStatus.Submitted, FinalScore = 4.0m, SubmittedAt = DateTime.UtcNow,
            SignoffStatus = ReviewSignoffStatus.NotStarted,
        });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext Db() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), n =>
                n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(_tc), new AuditInterceptor(HrUser()))
            .Options, _tc);

    private ICurrentUser HrUser()
    {
        var hr = Substitute.For<ICurrentUser>();
        hr.UserId.Returns(_hrUserA);
        hr.IsAuthenticated.Returns(true);
        hr.Email.Returns("hr@acme.com");
        hr.Permissions.Returns(new[] { PermissionCatalog.Performance.PublishAll });
        return hr;
    }

    private PerformanceCalibrationService Calibration(AppDbContext db) => new(
        db, _tc, HrUser(), Substitute.For<IPayrollAuditLogger>(),
        NullLogger<PerformanceCalibrationService>.Instance);

    [Fact]
    public async Task Calibration_written_under_tenant_A_is_invisible_to_tenant_B_and_round_trips()
    {
        // Apply a calibration under tenant A.
        _tc.TenantId = _tenantA;
        await using (var db = Db())
        {
            var result = await Calibration(db).ApplyAsync(
                new ApplyCalibrationInput(_cycleA, _empA, CalibratedScore: 3.5m, Reason: "Committee normalization."));
            result.IsSuccess.Should().BeTrue(result.Error);
            result.Value!.OriginalScore.Should().Be(4.0m);
        }

        // Fresh tenant-A context ⇒ real numeric read-back (not an in-memory ref).
        _tc.TenantId = _tenantA;
        await using (var readA = Db())
        {
            var calib = await readA.RatingCalibrations.AsNoTracking()
                .SingleAsync(c => c.CycleId == _cycleA && c.EmployeeId == _empA);
            calib.OriginalScore.Should().Be(4.0m);
            calib.CalibratedScore.Should().Be(3.5m);

            // The review's own score is untouched.
            var review = await readA.ManagerReviews.AsNoTracking().SingleAsync(r => r.Id == _reviewA);
            review.FinalScore.Should().Be(4.0m);
        }

        // Tenant B sees NOTHING through the global query filter (cross-tenant isolation).
        _tc.TenantId = _tenantB;
        await using (var readB = Db())
        {
            (await readB.RatingCalibrations.CountAsync()).Should().Be(0);

            // Even IgnoreQueryFilters confirms the row belongs to tenant A, never tenant B.
            var all = await readB.RatingCalibrations.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            all.Should().ContainSingle().Which.TenantId.Should().Be(_tenantA);
        }
    }
}
