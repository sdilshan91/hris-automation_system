// ============================================================================
// BUG-057 (US-PRF-001) — optimistic-concurrency regression for GoalService.UpdateAsync.
//
// The fix mirrors EmployeeService.UpdateProfileAsync: the update carries the client's
// read-time token (GoalInput.RowVersion) and the service sets it as the EF OriginalValue
// of the xmin concurrency token (Goal.Version, mapped .IsRowVersion()). A STALE token then
// raises DbUpdateConcurrencyException, surfaced as 409 "concurrency_conflict".
//
// HARNESS = real Postgres via Testcontainers (NOT InMemory). This is deliberate and load-
// bearing: xmin is a Postgres system column. The EF InMemory provider fires a *spurious*
// concurrency conflict for the .IsRowVersion() shim on any store-value divergence — so an
// InMemory test would go green even if the fix (the OriginalValue wiring) were reverted,
// i.e. it could not tell pre-fix from post-fix. Only Postgres exercises the real xmin guard:
//   • pre-fix  — the service re-reads the fresh xmin, so the guarded UPDATE always matches
//                and a stale write silently wins (no 409). This test FAILS pre-fix.
//   • post-fix — OriginalValue = the client's stale token, so UPDATE ... WHERE xmin = stale
//                matches 0 rows -> DbUpdateConcurrencyException -> 409. This test PASSES.
//
// Schema is built with EnsureCreatedAsync() (xmin is intrinsic to every Postgres table);
// MigrateAsync is intentionally avoided because an unrelated uncommitted model change was
// tripping PendingModelChangesWarning in the working tree.
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

public sealed class GoalConcurrencyPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _empId = Guid.NewGuid();
    private readonly Guid _deptId = Guid.NewGuid();
    private readonly Guid _jobTitleId = Guid.NewGuid();
    private readonly Guid _cycleId = Guid.NewGuid();
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
        // HR override so authorization passes without a manager linkage.
        _cu.Permissions.Returns(new[] { PermissionCatalog.Performance.SetGoalAll });

        await using var db = Db();
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        db.Tenants.Add(new Tenant { Id = _tenantId, Subdomain = "acme", Name = "Acme" });
        // Employee has required Department + JobTitle navigations; seed both first, else on real Postgres
        // the insert violates fk_employees_departments_department_id (mirrors EmployeeDirectorySortParamIssue019Tests).
        db.Departments.Add(new Department
        {
            Id = _deptId, TenantId = _tenantId, Name = "Engineering", Code = "ENG", IsActive = true, IsDeleted = false,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = _jobTitleId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true, IsDeleted = false,
        });
        db.Employees.Add(new Employee
        {
            Id = _empId, TenantId = _tenantId, EmployeeNo = "EMP-1",
            FirstName = "Ada", LastName = "Lovelace", Email = "ada@acme.com", Status = EmployeeStatus.Active,
            DepartmentId = _deptId, JobTitleId = _jobTitleId,
        });
        db.AppraisalCycles.Add(new AppraisalCycle
        {
            Id = _cycleId, TenantId = _tenantId, Name = "FY26", Status = AppraisalCycleStatus.Active,
            GoalSettingStart = now.AddDays(-5), GoalSettingEnd = now.AddDays(5), // window OPEN
            SelfAssessmentStart = now.AddDays(10), SelfAssessmentEnd = now.AddDays(20),
            RatingScaleMax = 5, SelfWeightPercent = 30,
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

    private GoalService Service(AppDbContext db) => new(db, _tc, _cu,
        Substitute.For<IPerformanceNotificationService>(), NullLogger<GoalService>.Instance);

    private GoalInput Input(string title, uint rowVersion) =>
        new(_cycleId, _empId, title, "desc", GoalCategory.Kpi, 50, "target", "%",
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), null, rowVersion);

    // Creates a goal and returns its (id, current xmin token).
    private async Task<(Guid Id, uint Token)> CreateGoalAsync(string title = "Original")
    {
        await using var db = Db();
        var created = await Service(db).CreateAsync(Input(title, rowVersion: 0));
        created.IsSuccess.Should().BeTrue(created.Error);
        return (created.Value!.Id, created.Value.RowVersion);
    }

    // ── Happy path: update with the CURRENT token succeeds ────────────────

    [Fact]
    public async Task UpdateGoal_CurrentToken_Succeeds_BUG057()
    {
        var (goalId, token) = await CreateGoalAsync();

        await using var db = Db();
        var result = await Service(db).UpdateAsync(goalId, Input("Revised", rowVersion: token));

        result.IsSuccess.Should().BeTrue(result.Error);
        result.Value!.Title.Should().Be("Revised");

        await using var verify = Db();
        (await verify.Goals.AsNoTracking().FirstAsync(g => g.Id == goalId)).Title.Should().Be("Revised");
    }

    // ── Conflict: a stale token is rejected with 409 ──────────────────────

    [Fact]
    public async Task UpdateGoal_StaleToken_Returns409_BUG057()
    {
        // Two "reads" hold the SAME token (e.g. two browser tabs opened the goal).
        var (goalId, token) = await CreateGoalAsync();

        // First writer commits with the token -> succeeds and bumps xmin.
        await using (var db1 = Db())
        {
            var first = await Service(db1).UpdateAsync(goalId, Input("First writer wins", rowVersion: token));
            first.IsSuccess.Should().BeTrue(first.Error);
        }

        // Second writer submits with the now-STALE token -> 409, change NOT applied.
        await using (var db2 = Db())
        {
            var second = await Service(db2).UpdateAsync(goalId, Input("Stale writer clobbers", rowVersion: token));
            second.IsFailure.Should().BeTrue("the stale token no longer matches the row's xmin");
            second.StatusCode.Should().Be(409);
            second.ErrorCode.Should().Be("concurrency_conflict");
        }

        // The first writer's change persisted; the stale write was rejected (no last-write-wins).
        await using var verify = Db();
        (await verify.Goals.AsNoTracking().FirstAsync(g => g.Id == goalId)).Title.Should().Be("First writer wins");
    }
}
