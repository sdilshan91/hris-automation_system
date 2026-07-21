// ============================================================================
// DF-37/ISSUE-280 — real-Postgres round-trip for the new payroll_slip_detail.component_code column.
//
// PayrollReportIntegrationTests proves the Basic/Allowances bucketing on InMemory (the IsBasic predicate is
// pure C#). This closes the gap both auditors flagged: nothing exercised a NON-NULL ComponentCode through the
// real varchar(50) schema. This seeds a slip detail with ComponentCode="BASIC" (display Name "Base Pay") on a
// real postgres:17-alpine, reloads it (proving the column round-trips), and runs the actual report — asserting
// the renamed-BASIC amount lands in the report's Basic bucket via Code, not Name.
// ============================================================================

using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.DTOs;
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

public sealed class PayrollReportComponentCodePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    private readonly Guid _tenantId = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

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
            .UseNpgsql(_postgres.GetConnectionString(), n => n.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantInterceptor(tc), new AuditInterceptor(cu))
            .Options, tc);
    }

    [Fact]
    [Trait("TC", "TC-PAY-011-16")]
    public async Task ComponentCode_RoundTrips_AndReportBucketsRenamedBasicByCode_OnPostgres()
    {
        var runId = Guid.NewGuid();
        var deptId = Guid.NewGuid();
        var titleId = Guid.NewGuid();
        var empId = Guid.NewGuid();
        var slipId = Guid.NewGuid();
        var basicDetailId = Guid.NewGuid();

        await using (var seed = CreateContext())
        {
            seed.PayrollRuns.Add(new PayrollRun
            {
                Id = runId, TenantId = _tenantId, PayMonth = 5, PayYear = 2026,
                Status = PayrollRunStatus.Finalized, InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
            });
            seed.Departments.Add(new Department { Id = deptId, TenantId = _tenantId, Name = "Engineering", Code = "ENG" });
            seed.JobTitles.Add(new JobTitle { Id = titleId, TenantId = _tenantId, TitleName = "Engineer", IsActive = true });
            seed.Employees.Add(new Employee
            {
                Id = empId, TenantId = _tenantId, EmployeeNo = "E1", FirstName = "E1", LastName = "X",
                Email = "e1@t.com", DateOfJoining = new DateTime(2020, 1, 1),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
                DepartmentId = deptId, JobTitleId = titleId,
            });
            seed.PayrollSlips.Add(new PayrollSlip
            {
                Id = slipId, TenantId = _tenantId, PayrollRunId = runId, EmployeeId = empId,
                GrossEarnings = 50_000m, TotalDeductions = 0m, NetSalary = 50_000m,
                WorkingDays = 22, PaidDays = 22, LopDays = 0, PayMonth = 5, PayYear = 2026,
            });
            // A RENAMED basic component: Code "BASIC", display Name "Base Pay".
            seed.PayrollSlipDetails.Add(new PayrollSlipDetail
            {
                Id = basicDetailId, TenantId = _tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(),
                ComponentCode = "BASIC", ComponentName = "Base Pay",
                ComponentType = nameof(SalaryComponentType.Earning), Amount = 40_000m,
            });
            // A non-basic component misleadingly NAMED "Basic" (Code "HRA").
            seed.PayrollSlipDetails.Add(new PayrollSlipDetail
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, PayrollSlipId = slipId, SalaryComponentId = Guid.NewGuid(),
                ComponentCode = "HRA", ComponentName = "Basic",
                ComponentType = nameof(SalaryComponentType.Earning), Amount = 10_000m,
            });
            await seed.SaveChangesAsync();
        }

        // 1. The varchar(50) column round-trips a non-null value through the real schema.
        await using (var verify = CreateContext())
        {
            var reloaded = await verify.PayrollSlipDetails.AsNoTracking().SingleAsync(d => d.Id == basicDetailId);
            reloaded.ComponentCode.Should().Be("BASIC");
        }

        // 2. The real report buckets the renamed BASIC (by Code) as Basic; the misleadingly-named HRA as Allowances.
        await using (var reportDb = CreateContext())
        {
            var cu = Substitute.For<ICurrentUser>();
            cu.IsAuthenticated.Returns(true);
            cu.UserId.Returns(Guid.NewGuid());
            var ctx = new MutableTenantContext { TenantId = _tenantId };
            var audit = new PayrollAuditLogger(reportDb, ctx, cu, NullLogger<PayrollAuditLogger>.Instance);
            var svc = new PayrollReportService(reportDb, ctx, audit, NullLogger<PayrollReportService>.Instance);

            var report = (await svc.GenerateReportAsync(PayrollReportType.PayrollSummary,
                new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 })).Value!;

            var total = report.TotalRow!.Cells;
            total[2].Should().Be("40000.00", "the Code=BASIC line buckets as Basic even though its Name is 'Base Pay'");
            total[3].Should().Be("10000.00", "the Name='Basic' HRA line (Code=HRA) buckets as Allowances");
        }
    }
}
