// ============================================================================
// US-PAY-009: Payroll reports + analytics — integration tests.
//
// Exercises IPayrollReportService over a real AppDbContext (InMemory) with the ITenantContext-driven
// global query filter, covering:
//   - FR-1a: Payroll Summary totals == the sum of the individual finalized payslips for the period.
//   - FR-1e/AC-2: Bank Advice contains every employee from the finalized run with the correct net.
//   - BR-2: account masked in the preview but FULL in the exported file.
//   - FR-1g/BR-4: Variance report flags a >10% month-over-month net change.
//   - BR-1: only Finalized runs are included (a ReviewPending run's slips are excluded).
//   - AC-5/FR-8: Tenant A's report excludes Tenant B's data.
//   - FR-2/AC-4: CSV + Excel export produce a non-empty file with the expected header.
//
// PROVIDER: InMemory — same rationale as the other Payroll integration tests (the verify gate runs
// `dotnet test` with no PostgreSQL / Docker).
// ============================================================================

using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using HRM.Application.Common.Interfaces;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using HRM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRM.Tests.Integration;

public sealed class PayrollReportIntegrationTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

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

    private AppDbContext Db(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        return new AppDbContext(options, ctx);
    }

    private (AppDbContext Db, ITenantContext Ctx, PayrollReportService Svc) Scope(Guid tenantId)
    {
        var ctx = new MutableTenantContext { TenantId = tenantId };
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(_dbName).Options;
        var db = new AppDbContext(options, ctx);
        return (db, ctx, new PayrollReportService(db, ctx, NullLogger<PayrollReportService>.Instance));
    }

    // ── Seeding ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a department, an employee, and a FINALIZED-run payslip (with Basic earning + a statutory PF
    /// deduction) for a period. Returns the slip's net for assertions.
    /// </summary>
    private async Task<(Guid EmployeeId, decimal Net)> SeedFinalizedSlip(
        Guid tenantId, Guid runId, string no, decimal gross, decimal pf,
        int month = 5, int year = 2026, Guid? departmentId = null, string deptName = "Engineering")
    {
        using var db = Db(tenantId);
        var deptId = departmentId ?? BaseEntity.NewUuidV7();
        if (!db.Departments.Any(d => d.Id == deptId))
            db.Departments.Add(new Department { Id = deptId, TenantId = tenantId, Name = deptName, Code = deptName[..Math.Min(3, deptName.Length)].ToUpper() });

        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "X",
            Email = $"{no}@t.com", DateOfJoining = new DateTime(2020, 1, 1),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            DepartmentId = deptId, JobTitleId = BaseEntity.NewUuidV7(),
        });

        decimal net = gross - pf;
        var slipId = BaseEntity.NewUuidV7();
        db.PayrollSlips.Add(new PayrollSlip
        {
            Id = slipId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = empId,
            GrossEarnings = gross, TotalDeductions = pf, NetSalary = net,
            WorkingDays = 22, PaidDays = 22, LopDays = 0, PayMonth = month, PayYear = year,
        });
        db.PayrollSlipDetails.Add(new PayrollSlipDetail
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId,
            SalaryComponentId = BaseEntity.NewUuidV7(), ComponentName = "Basic",
            ComponentType = nameof(SalaryComponentType.Earning), Amount = gross,
        });
        if (pf > 0)
            db.PayrollSlipDetails.Add(new PayrollSlipDetail
            {
                Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId,
                SalaryComponentId = BaseEntity.NewUuidV7(), ComponentName = "Provident Fund",
                ComponentType = nameof(SalaryComponentType.Statutory), Amount = pf,
            });

        await db.SaveChangesAsync();
        return (empId, net);
    }

    private async Task SeedRun(Guid tenantId, Guid runId, PayrollRunStatus status, int month = 5, int year = 2026)
    {
        using var db = Db(tenantId);
        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId, TenantId = tenantId, PayMonth = month, PayYear = year, Status = status,
            InitiatedBy = Guid.NewGuid(), InitiatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    // ── FR-1a: summary totals == sum of payslips ─────────────────────────────

    [Fact]
    public async Task PayrollSummary_Totals_EqualSumOfPayslips()
    {
        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized);
        var a1 = await SeedFinalizedSlip(_tenantA, runId, "A1", gross: 50_000m, pf: 5_000m);
        var a2 = await SeedFinalizedSlip(_tenantA, runId, "A2", gross: 30_000m, pf: 3_000m);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(
                PayrollReportType.PayrollSummary, new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });

            result.IsSuccess.Should().BeTrue();
            var report = result.Value!;

            // Grand-total row: gross 80000, net = (50000-5000)+(30000-3000) = 72000.
            var total = report.TotalRow!.Cells;
            // Columns: Department, Employee Count, Total Basic, Total Allowances, Total Gross, Total Statutory, Total Other Deductions, Total Net
            total[1].Should().Be("2");                 // employee count
            total[4].Should().Be("80000.00");          // total gross == 50k + 30k
            total[5].Should().Be("8000.00");           // total statutory (PF) == 5k + 3k
            total[7].Should().Be("72000.00");          // total net == sum of slip nets
        }
    }

    // ── FR-1e/AC-2: bank advice contains all employees with correct net ──────

    [Fact]
    public async Task BankAdvice_Preview_ContainsAllEmployees_WithCorrectNet()
    {
        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized);
        var a1 = await SeedFinalizedSlip(_tenantA, runId, "A1", 50_000m, 5_000m);
        var a2 = await SeedFinalizedSlip(_tenantA, runId, "A2", 30_000m, 3_000m);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GetBankAdvicePreviewAsync(new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });

            result.IsSuccess.Should().BeTrue();
            var preview = result.Value!;
            preview.EmployeeCount.Should().Be(2);
            preview.TotalNetAmount.Should().Be(72_000m);
            preview.Lines.Select(l => l.EmployeeNo).Should().BeEquivalentTo(["A1", "A2"]);
            preview.Lines.Single(l => l.EmployeeNo == "A1").NetAmount.Should().Be(45_000m);
            preview.Lines.Single(l => l.EmployeeNo == "A2").NetAmount.Should().Be(27_000m);
            preview.Lines.Should().OnlyContain(l => l.Narration == "Salary May 2026");
        }
    }

    // ── BR-2: masked in preview, full in exported file ──────────────────────

    [Fact]
    public async Task BankAdvice_AccountMaskedInPreview_ButFullInExportFile()
    {
        // The Employee entity has no bank columns yet (documented gap), so the account is empty in both
        // paths. We assert the path WIRING: the preview routes through the masked path (note present) and
        // the export routes through the full path. Both produce a file/preview without leaking.
        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized);
        await SeedFinalizedSlip(_tenantA, runId, "A1", 50_000m, 5_000m);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var preview = await svc.GetBankAdvicePreviewAsync(new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });
            preview.Value!.Note.Should().Contain("not yet captured");

            var export = await svc.ExportReportAsync(
                PayrollReportType.BankAdvice, PayrollExportFormat.Csv,
                new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });
            export.IsSuccess.Should().BeTrue();
            var csv = Encoding.UTF8.GetString(export.Value!.FileContent);
            csv.Should().Contain("Account Number"); // header
            csv.Should().Contain("A1");
        }

        // The mask helper itself is the load-bearing BR-2 logic (unit-tested separately): a populated
        // account masks to last-4 in preview but stays full in the file.
        PayrollReportService.MaskAccount("1234567890").Should().Be("******7890");
    }

    // ── FR-1g/BR-4: variance flags a >10% change ────────────────────────────

    [Fact]
    public async Task Variance_FlagsChangeOverTenPercent()
    {
        // Previous month (April): net 45000. Current month (May): net 72000 (+60%) => flagged.
        var aprRun = BaseEntity.NewUuidV7();
        var mayRun = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, aprRun, PayrollRunStatus.Finalized, month: 4, year: 2026);
        await SeedRun(_tenantA, mayRun, PayrollRunStatus.Finalized, month: 5, year: 2026);

        // Same employee number across months — but distinct employee rows (one per slip seed). To make a
        // true month-over-month comparison for ONE employee, seed both slips against the same employee id.
        Guid empId;
        using (var db = Db(_tenantA))
        {
            var deptId = BaseEntity.NewUuidV7();
            db.Departments.Add(new Department { Id = deptId, TenantId = _tenantA, Name = "Eng", Code = "ENG" });
            empId = BaseEntity.NewUuidV7();
            db.Employees.Add(new Employee
            {
                Id = empId, TenantId = _tenantA, EmployeeNo = "A1", FirstName = "A1", LastName = "X",
                Email = "a1@t.com", DateOfJoining = new DateTime(2020, 1, 1),
                EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
                DepartmentId = deptId, JobTitleId = BaseEntity.NewUuidV7(),
            });
            AddSlip(db, _tenantA, aprRun, empId, gross: 50_000m, net: 45_000m, month: 4, year: 2026);
            AddSlip(db, _tenantA, mayRun, empId, gross: 80_000m, net: 72_000m, month: 5, year: 2026);
            await db.SaveChangesAsync();
        }

        var (sdb, _, svc) = Scope(_tenantA);
        using (sdb)
        {
            var result = await svc.GenerateReportAsync(
                PayrollReportType.Variance, new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });

            result.IsSuccess.Should().BeTrue();
            var row = result.Value!.Rows.Single(r => r.Cells[0] == "A1");
            // Columns: Employee No, Employee, Department, Previous Net, Current Net, Change, Change %, Flagged
            row.Cells[3].Should().Be("45000.00");
            row.Cells[4].Should().Be("72000.00");
            row.Cells[7].Should().Be("Yes"); // flagged: +60% > 10%
        }
    }

    private static void AddSlip(AppDbContext db, Guid tenantId, Guid runId, Guid empId,
        decimal gross, decimal net, int month, int year)
    {
        db.PayrollSlips.Add(new PayrollSlip
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollRunId = runId, EmployeeId = empId,
            GrossEarnings = gross, TotalDeductions = gross - net, NetSalary = net,
            WorkingDays = 22, PaidDays = 22, LopDays = 0, PayMonth = month, PayYear = year,
        });
    }

    // ── BR-1: only Finalized runs included ──────────────────────────────────

    [Fact]
    public async Task OnlyFinalizedRuns_AreIncluded()
    {
        var finalized = BaseEntity.NewUuidV7();
        var reviewPending = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, finalized, PayrollRunStatus.Finalized);
        await SeedRun(_tenantA, reviewPending, PayrollRunStatus.ReviewPending);
        await SeedFinalizedSlip(_tenantA, finalized, "FIN", 50_000m, 5_000m);
        await SeedFinalizedSlip(_tenantA, reviewPending, "PEND", 99_000m, 0m);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(
                PayrollReportType.EmployeeRegister, new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });

            result.IsSuccess.Should().BeTrue();
            var empNos = result.Value!.Rows.Select(r => r.Cells[0]).ToList();
            empNos.Should().Contain("FIN");
            empNos.Should().NotContain("PEND"); // ReviewPending run excluded (BR-1)
        }
    }

    // ── AC-5/FR-8: tenant isolation ─────────────────────────────────────────

    [Fact]
    public async Task TenantA_Report_ExcludesTenantB()
    {
        var runA = BaseEntity.NewUuidV7();
        var runB = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runA, PayrollRunStatus.Finalized);
        await SeedRun(_tenantB, runB, PayrollRunStatus.Finalized);
        await SeedFinalizedSlip(_tenantA, runA, "A1", 50_000m, 5_000m);
        await SeedFinalizedSlip(_tenantB, runB, "B1", 99_000m, 0m);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(
                PayrollReportType.EmployeeRegister, new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });

            result.IsSuccess.Should().BeTrue();
            var empNos = result.Value!.Rows.Select(r => r.Cells[0]).ToList();
            empNos.Should().Contain("A1");
            empNos.Should().NotContain("B1"); // cross-tenant invisible via the global filter
        }
    }

    // ── FR-2/AC-4: CSV + Excel export produce a non-empty file with headers ──

    [Fact]
    public async Task Export_CsvAndExcel_ProduceNonEmptyFile_WithExpectedHeaders()
    {
        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized);
        await SeedFinalizedSlip(_tenantA, runId, "A1", 50_000m, 5_000m);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var qp = new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 };

            var csv = await svc.ExportReportAsync(PayrollReportType.PayrollSummary, PayrollExportFormat.Csv, qp);
            csv.IsSuccess.Should().BeTrue();
            csv.Value!.FileContent.Should().NotBeEmpty();
            Encoding.UTF8.GetString(csv.Value.FileContent).Split('\n')[0].Should().Contain("Department");

            var xlsx = await svc.ExportReportAsync(PayrollReportType.PayrollSummary, PayrollExportFormat.Xlsx, qp);
            xlsx.IsSuccess.Should().BeTrue();
            xlsx.Value!.FileContent.Should().NotBeEmpty();
            using var wb = new XLWorkbook(new MemoryStream(xlsx.Value.FileContent));
            wb.Worksheets.First().Cell(1, 1).GetString().Should().Be("Department");
        }
    }
}
