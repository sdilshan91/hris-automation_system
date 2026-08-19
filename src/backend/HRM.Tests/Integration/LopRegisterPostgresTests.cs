// ============================================================================
// US-LV-011 (queue B2): cross-employee LOP register — the isolation + INCLUDE-TRAP arms, on REAL Postgres.
//
// These three arms MUST run on Testcontainers-Postgres, not InMemory:
//   - Tenant isolation (Critical Rule #1): tenant A's register never returns tenant B's rows. Proven by
//     the EF global query filter against a real DB. This is the mutation-verified arm.
//   - A soft-deleted EMPLOYEE is excluded from the register (its identity can't be resolved).
//   - THE INCLUDE TRAP (TEST-FINDINGS.md): an employee whose related filtered principal (its JobTitle) is
//     soft-deleted must STILL appear — mirrors Feedback360's
//     ASoftDeletedJobTitle_LeavesTheLabelBlank_ButTheRevieweeStillLoads. Employee.JobTitleId is a REQUIRED
//     navigation with a global query filter, so an Include(lr => lr.Employee) that then joins JobTitle emits
//     an INNER JOIN and silently drops the row. InMemory enforces neither the FK nor that join, so it would
//     pass over the bug; only real PG proves the register resolves identity via a separate query.
//
// Harness mirrors LopAuthorityPayrollPostgresTests: one postgres:17-alpine container, migrations once,
// UseSnakeCaseNamingConvention(), production interceptors. Db(tenant)/Svc(tenant) are parameterised so we
// can seed + query under distinct tenant scopes.
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

public sealed class LopRegisterPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    private static readonly DateOnly From = new(2026, 3, 1);
    private static readonly DateOnly To = new(2026, 3, 31);
    private static readonly DateOnly Mid = new(2026, 3, 10);

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = Db(_tenantA);
        await db.Database.MigrateAsync();
        db.Tenants.Add(new Tenant { Id = _tenantA, Subdomain = $"a{_tenantA:N}"[..20], Name = "A", DefaultCountryCode = "LK" });
        db.Tenants.Add(new Tenant { Id = _tenantB, Subdomain = $"b{_tenantB:N}"[..20], Name = "B", DefaultCountryCode = "LK" });
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── harness ─────────────────────────────────────────────────────────────
    private sealed class TC : ITenantContext
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

    private sealed class CU : ICurrentUser
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public string Email => "hr@t.com";
        public Guid TenantId { get; init; }
        public Guid UserTenantId => TenantId;
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
    }

    private AppDbContext Db(Guid tenantId)
    {
        var tc = new TC { TenantId = tenantId };
        var cu = new CU { TenantId = tenantId };
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString(), n =>
                {
                    n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                    n.EnableRetryOnFailure(maxRetryCount: 3);
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
                .Options, tc);
    }

    private LopService Svc(AppDbContext db, Guid tenantId) => new(
        db, new TC { TenantId = tenantId }, new CU { TenantId = tenantId },
        Substitute.For<ILeaveTypeService>(), Substitute.For<IAttendanceProvider>(),
        Substitute.For<ILeaveNotificationService>(), NullLogger<LopService>.Instance,
        Substitute.For<ITenantLeaveYearResolver>());

    // ── seeding ─────────────────────────────────────────────────────────────
    private async Task<Guid> SeedLeaveType(Guid tenantId)
    {
        await using var db = Db(tenantId);
        var id = BaseEntity.NewUuidV7();
        db.LeaveTypes.Add(new LeaveType
        {
            Id = id, TenantId = tenantId, Name = "Loss of Pay", Code = "LOP",
            AnnualEntitlement = 0m, SystemCategory = LeaveTypeSystemCategory.LossOfPay, IsActive = true,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<(Guid deptId, Guid titleId)> SeedDeptAndTitle(Guid tenantId)
    {
        await using var db = Db(tenantId);
        var deptId = BaseEntity.NewUuidV7();
        var titleId = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = "Engineering", Code = "ENG" });
        db.JobTitles.Add(new JobTitle { Id = titleId, TenantId = tenantId, TitleName = "Engineer" });
        await db.SaveChangesAsync();
        return (deptId, titleId);
    }

    private async Task<Guid> SeedEmployee(Guid tenantId, Guid deptId, Guid titleId, string no, string first, string last)
    {
        await using var db = Db(tenantId);
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, EmployeeNo = no, FirstName = first, LastName = last,
            Email = $"{no}@t.com".ToLowerInvariant(), DateOfJoining = new DateTime(2020, 1, 1),
            DepartmentId = deptId, JobTitleId = titleId, LocationId = null,
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
        });
        await db.SaveChangesAsync();
        return empId;
    }

    private async Task SeedLop(Guid tenantId, Guid empId, Guid leaveTypeId, DateOnly date)
    {
        await using var db = Db(tenantId);
        db.LeaveRequests.Add(new LeaveRequest
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = empId, LeaveTypeId = leaveTypeId,
            StartDate = date, EndDate = date, TotalDays = 1m, Status = LeaveRequestStatus.HrAssigned,
            RequestedAt = DateTime.UtcNow, IsLop = true, LopSource = LopSource.HrAssigned,
        });
        await db.SaveChangesAsync();
    }

    // ── Critical Rule #1: tenant isolation (mutation-verified arm) ────────────
    [Fact]
    public async Task Register_TenantA_NeverReturnsTenantBRows_OnPostgres()
    {
        var ltA = await SeedLeaveType(_tenantA);
        var ltB = await SeedLeaveType(_tenantB);
        var (deptA, titleA) = await SeedDeptAndTitle(_tenantA);
        var (deptB, titleB) = await SeedDeptAndTitle(_tenantB);
        var empA = await SeedEmployee(_tenantA, deptA, titleA, "A-1", "Ann", "Archer");
        var empB = await SeedEmployee(_tenantB, deptB, titleB, "B-1", "Ben", "Boone");
        await SeedLop(_tenantA, empA, ltA, Mid);
        await SeedLop(_tenantB, empB, ltB, Mid);

        await using var db = Db(_tenantA);
        var result = await Svc(db, _tenantA).GetLopRegisterAsync(From, To);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle()
            .Which.EmployeeId.Should().Be(empA);
        result.Value.Should().NotContain(r => r.EmployeeId == empB,
            "the EF global query filter must scope the register to tenant A only");
    }

    // ── a soft-deleted employee is excluded ──────────────────────────────────
    [Fact]
    public async Task Register_ExcludesSoftDeletedEmployee_OnPostgres()
    {
        var lt = await SeedLeaveType(_tenantA);
        var (dept, title) = await SeedDeptAndTitle(_tenantA);
        var live = await SeedEmployee(_tenantA, dept, title, "LIVE", "Live", "One");
        var gone = await SeedEmployee(_tenantA, dept, title, "GONE", "Gone", "Two");
        await SeedLop(_tenantA, live, lt, Mid);
        await SeedLop(_tenantA, gone, lt, Mid);

        await using (var d = Db(_tenantA))
        {
            var emp = await d.Employees.FirstAsync(e => e.Id == gone);
            emp.IsDeleted = true;
            await d.SaveChangesAsync();
        }

        await using var db = Db(_tenantA);
        var result = await Svc(db, _tenantA).GetLopRegisterAsync(From, To);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle()
            .Which.EmployeeId.Should().Be(live);
    }

    // ── THE INCLUDE TRAP: soft-deleted JobTitle must NOT erase the employee ───
    [Fact]
    public async Task Register_EmployeeWithSoftDeletedJobTitle_StillAppears_OnPostgres()
    {
        var lt = await SeedLeaveType(_tenantA);
        var (dept, title) = await SeedDeptAndTitle(_tenantA);
        var emp = await SeedEmployee(_tenantA, dept, title, "TRAP", "Trap", "Guard");
        await SeedLop(_tenantA, emp, lt, Mid);

        // Soft-delete the employee's (required, query-filtered) JobTitle. An Include(lr => lr.Employee) that
        // joins JobTitle would INNER JOIN this away and drop the whole row; resolving identity separately does not.
        await using (var d = Db(_tenantA))
        {
            var jt = await d.JobTitles.FirstAsync(j => j.Id == title);
            jt.IsDeleted = true;
            await d.SaveChangesAsync();
        }

        await using var db = Db(_tenantA);
        var result = await Svc(db, _tenantA).GetLopRegisterAsync(From, To);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Should().ContainSingle(
            "a soft-deleted job title must not erase the employee from the register");
        result.Value![0].EmployeeId.Should().Be(emp);
        result.Value![0].EmployeeName.Should().Be("Trap Guard", "the rest of the record is unaffected");
        result.Value![0].EmployeeNo.Should().Be("TRAP");
    }
}
