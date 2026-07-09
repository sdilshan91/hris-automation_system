// ============================================================================
// BUG-260 (US-PRF-004) — the Departments-scope selection round-trips through the jsonb column.
//
// AppraisalCycle.ScopeDepartmentIds (List<Guid>) is mapped to a jsonb column ("scope_department_ids",
// AppraisalCycleConfiguration.HasColumnType("jsonb")). The EF InMemory provider stores the List<Guid> as
// a live in-memory reference, so an InMemory test proves the SERVICE writes the selection but NOT that the
// jsonb column serializes/deserializes on a real store. This Postgres (Testcontainers) test closes that gap:
// it creates a Departments-scoped cycle through the service, then re-reads ScopeDepartmentIds from a FRESH
// context — which forces a real jsonb read-back — proving the column round-trips (not just an object ref).
//
// HARNESS = real Postgres via Testcontainers (NOT InMemory), same rationale + pattern as
// GoalConcurrencyPostgresTests. Schema is built with EnsureCreatedAsync() (the jsonb column comes from the
// entity configuration); MigrateAsync is intentionally avoided to sidestep PendingModelChangesWarning on any
// unrelated uncommitted model drift in the working tree — the migration itself is exercised by the
// `migrations` CI job + DbInitializer at startup.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Performance.DTOs;
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

public sealed class AppraisalCycleScopePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _empHr = Guid.NewGuid();
    private readonly Guid _empEng = Guid.NewGuid();
    private readonly Guid _deptHr = Guid.NewGuid();
    private readonly Guid _deptEng = Guid.NewGuid();
    private readonly Guid _jobTitleId = Guid.NewGuid();
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
        // Employee has required Department + JobTitle FKs; seed both first (mirrors GoalConcurrencyPostgresTests).
        db.Departments.Add(new Department
        {
            Id = _deptHr, TenantId = _tenantId, Name = "HR", Code = "HR", IsActive = true, IsDeleted = false,
        });
        db.Departments.Add(new Department
        {
            Id = _deptEng, TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true, IsDeleted = false,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = _jobTitleId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true, IsDeleted = false,
        });
        db.Employees.Add(new Employee
        {
            Id = _empHr, TenantId = _tenantId, EmployeeNo = "HR-1", FirstName = "Ada", LastName = "L",
            Email = "ada@acme.com", Status = EmployeeStatus.Active, DepartmentId = _deptHr, JobTitleId = _jobTitleId,
        });
        db.Employees.Add(new Employee
        {
            Id = _empEng, TenantId = _tenantId, EmployeeNo = "ENG-1", FirstName = "Alan", LastName = "T",
            Email = "alan@acme.com", Status = EmployeeStatus.Active, DepartmentId = _deptEng, JobTitleId = _jobTitleId,
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

    private AppraisalCycleService Service(AppDbContext db) => new(db, _tc, _cu,
        Substitute.For<IPerformanceNotificationService>(), NullLogger<AppraisalCycleService>.Instance);

    private static IReadOnlyList<CyclePhaseInput> Phases(DateTime start) =>
    [
        new(CyclePhaseType.GoalSetting, start, start.AddDays(10)),
        new(CyclePhaseType.SelfAssessment, start.AddDays(11), start.AddDays(20)),
        new(CyclePhaseType.ManagerReview, start.AddDays(21), start.AddDays(30)),
    ];

    // ── BUG-260: jsonb round-trip ─────────────────────────────────────────

    [Fact]
    public async Task DepartmentsScope_ScopeDepartmentIds_RoundTripsThroughJsonbColumn_BUG260()
    {
        var start = DateTime.UtcNow.Date.AddDays(1);
        var scope = new ParticipantScopeInput(
            ParticipantScopeType.Departments, DepartmentIds: new[] { _deptHr, _deptEng });

        Guid cycleId;
        await using (var db = Db())
        {
            var created = await Service(db).CreateAsync(new CreateCycleInput(
                "FY26 Dept", CycleType.Annual, start, start.AddDays(40), Phases(start), scope,
                RatingScaleMax: 5, SelfWeightPercent: 30,
                Is360Enabled: false, IsCalibrationEnabled: false, IsAnonymousFeedback: false));
            created.IsSuccess.Should().BeTrue(created.Error);
            cycleId = created.Value!.Id;
        }

        // Re-read from a FRESH context ⇒ this is a real jsonb read-back from Postgres, not an in-memory ref.
        await using (var verify = Db())
        {
            var cycle = await verify.AppraisalCycles.AsNoTracking().FirstAsync(c => c.Id == cycleId);
            cycle.ScopeDepartmentIds.Should().BeEquivalentTo(new[] { _deptHr, _deptEng });
        }

        // The read DTO exposes the full scope off the persisted jsonb selection + the resolved participants.
        await using (var read = Db())
        {
            var dto = (await Service(read).GetByIdAsync(cycleId)).Value!;
            dto.Scope.ScopeType.Should().Be(ParticipantScopeType.Departments);
            dto.Scope.DepartmentIds.Should().BeEquivalentTo(new[] { _deptHr, _deptEng });
            dto.Scope.EmployeeIds.Should().BeEquivalentTo(new[] { _empHr, _empEng });
        }
    }
}
