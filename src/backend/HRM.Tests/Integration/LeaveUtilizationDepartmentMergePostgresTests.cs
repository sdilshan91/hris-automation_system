// ============================================================================
// DF-issue194-pg-arm — the case-insensitive department merge, on REAL Postgres.
//
// ISSUE-194: "Engineering" and "engineering" rendered as two bars, each with a utilization percentage computed
// over a fraction of the real entitlement. The fix groups case-insensitively — but only because the grouping
// runs on a MATERIALISED list. `StringComparer.OrdinalIgnoreCase` is a LINQ-to-Objects concept: pushed into an
// IQueryable it either fails to translate or is silently dropped under Postgres's default (case-sensitive)
// collation.
//
// The only existing coverage is LeaveReportServiceTests, which runs on EF InMemory — where string comparison
// is ordinal in memory anyway, so the merge appears to work whether or not it survives translation. That is
// this repo's recurring "InMemory masks Postgres" class, and it is exactly what the production comment at
// LeaveReportService.cs:707-712 warns about.
//
// This arm runs the real service against real Postgres, so a regression that moves the GroupBy into the query
// fails here rather than in a customer's chart.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveReports.DTOs;
using HRM.Domain.Authorization;
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

[Trait("TC", "TC-LV-ISSUE194-PG")]
public sealed class LeaveUtilizationDepartmentMergePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync() => await _postgres.StartAsync();
    public async Task DisposeAsync() => await _postgres.DisposeAsync();

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

    private AppDbContext CreateContext()
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

    private LeaveReportService BuildService(AppDbContext db)
    {
        var tc = new MutableTenantContext { TenantId = _tenantId };
        var cu = Substitute.For<ICurrentUser>();
        cu.IsAuthenticated.Returns(true);
        cu.UserId.Returns(Guid.NewGuid());
        cu.TenantId.Returns(_tenantId);
        // Without Reports.ViewAll, ResolveScopeAsync falls back to an EMPTY Employee scope (no employee row
        // matches the substituted user id) and the chart comes back with zero points — the arm would then
        // "pass" for a reason that has nothing to do with grouping.
        cu.Permissions.Returns(new[] { PermissionCatalog.Reports.ViewAll });

        // Entitlement resolution has its own tests; a flat 10 days per employee keeps the arithmetic obvious
        // so the assertion is about GROUPING, not about entitlement.
        var entitlements = Substitute.For<ILeaveEntitlementService>();
        entitlements
            .ComputeProratedEntitlementsBatchAsync(
                Arg.Any<IReadOnlyList<Employee>>(), Arg.Any<IReadOnlyList<LeaveType>>(),
                Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var emps = ci.ArgAt<IReadOnlyList<Employee>>(0);
                var types = ci.ArgAt<IReadOnlyList<LeaveType>>(1);
                return emps
                    .SelectMany(e => types.Select(t => (e.Id, t.Id)))
                    .ToDictionary(k => (EmployeeId: k.Item1, LeaveTypeId: k.Item2), _ => 10m);
            });

        return new LeaveReportService(
            db, tc, cu, entitlements,
            Substitute.For<IReportExportStorage>(),
            NullLogger<LeaveReportService>.Instance,
            new TenantLeaveYearResolver(db, tc));
    }

    [Fact]
    public async Task Departments_DifferingOnlyInCASE_MergeIntoOneChartPoint_OnPostgres_ISSUE194()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var jobId = BaseEntity.NewUuidV7();
        db.Tenants.Add(new Tenant
        {
            Id = _tenantId, Subdomain = "acme", Name = "Acme",
            DefaultCountryCode = "LK", FiscalYearStartMonth = 1,
        });
        db.JobTitles.Add(new JobTitle
        {
            Id = jobId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true,
        });

        // The whole point: two department rows that differ ONLY in case. Postgres's default collation treats
        // these as distinct, so a database-side GROUP BY would emit two bars.
        var deptUpper = BaseEntity.NewUuidV7();
        var deptLower = BaseEntity.NewUuidV7();
        db.Departments.Add(new Department
        {
            Id = deptUpper, TenantId = _tenantId, Name = "Engineering", Code = "ENG1", IsActive = true,
        });
        db.Departments.Add(new Department
        {
            Id = deptLower, TenantId = _tenantId, Name = "engineering", Code = "ENG2", IsActive = true,
        });

        var leaveType = new LeaveType
        {
            Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, Name = "Annual",
            AnnualEntitlement = 10m, AccrualFrequency = AccrualFrequency.Upfront,
            Gender = LeaveTypeGender.All, IsActive = true,
        };
        db.LeaveTypes.Add(leaveType);

        foreach (var (deptId, no) in new[] { (deptUpper, "E-UPPER"), (deptLower, "E-LOWER") })
        {
            db.Employees.Add(new Employee
            {
                Id = BaseEntity.NewUuidV7(), TenantId = _tenantId, EmployeeNo = no,
                FirstName = "P", LastName = no, Email = $"{no}@t.com",
                DateOfJoining = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DepartmentId = deptId, JobTitleId = jobId, EmploymentType = EmploymentType.FullTime,
                Status = EmployeeStatus.Active, IsActive = true, Fte = 1.0m,
            });
        }

        await db.SaveChangesAsync();

        var result = await BuildService(db).GetAnalyticsAsync(
            LeaveAnalyticsChartType.UtilizationByDepartment,
            new LeaveReportQueryParams { Year = 2026 });

        result.IsSuccess.Should().BeTrue();

        var engineeringPoints = result.Value!.Points
            .Where(p => string.Equals(p.Label, "Engineering", StringComparison.OrdinalIgnoreCase))
            .ToList();

        engineeringPoints.Should().HaveCount(1,
            "\"Engineering\" and \"engineering\" are one department to a human reader — two bars is ISSUE-194, "
            + "and under Postgres's case-sensitive collation that is exactly what a database-side GROUP BY "
            + "would produce while the InMemory test kept passing");
    }
}
