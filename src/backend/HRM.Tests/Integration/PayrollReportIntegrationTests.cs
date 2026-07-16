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

    private readonly Guid _actor = Guid.NewGuid();

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
        public IReadOnlyList<string> Permissions => [];
        public bool IsAuthenticated => true;
        public bool IsImpersonating => false;
        public Guid? ImpersonatorId => null;
        public Guid? ImpersonationSessionId => null;
        public bool ImpersonationReadOnly => false;
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
        var audit = new PayrollAuditLogger(
            db, ctx, new FakeCurrentUser { TenantId = tenantId, UserId = _actor },
            NullLogger<PayrollAuditLogger>.Instance);
        return (db, ctx, new PayrollReportService(db, ctx, audit, NullLogger<PayrollReportService>.Instance));
    }

    // ── Seeding ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a department, an employee, and a FINALIZED-run payslip (with Basic earning + a statutory PF
    /// deduction) for a period. Returns the slip's net for assertions.
    /// </summary>
    private async Task<(Guid EmployeeId, decimal Net)> SeedFinalizedSlip(
        Guid tenantId, Guid runId, string no, decimal gross, decimal pf,
        int month = 5, int year = 2026, Guid? departmentId = null, string deptName = "Engineering",
        string? bankAccountNumber = null, string? bankName = null, string? bankBranchCode = null)
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
            BankAccountNumber = bankAccountNumber, BankName = bankName, BankBranchCode = bankBranchCode,
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
        // BR-2: the masked preview shows only the last 4 digits; the exported file carries the FULL number.
        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized);
        await SeedFinalizedSlip(_tenantA, runId, "A1", 50_000m, 5_000m,
            bankAccountNumber: "1234567890", bankName: "Acme Bank", bankBranchCode: "BR001");

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var preview = await svc.GetBankAdvicePreviewAsync(new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });
            preview.IsSuccess.Should().BeTrue();
            preview.Value!.Masked.Should().BeTrue();
            var line = preview.Value.Lines.Single(l => l.EmployeeNo == "A1");
            line.AccountNumber.Should().Be("******7890"); // masked: last-4 only (BR-2)
            line.AccountNumber.Should().NotBe("1234567890"); // full number NOT leaked in the preview

            var export = await svc.ExportReportAsync(
                PayrollReportType.BankAdvice, PayrollExportFormat.Csv,
                new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });
            export.IsSuccess.Should().BeTrue();
            var csv = Encoding.UTF8.GetString(export.Value!.FileContent);
            csv.Should().Contain("Account Number"); // header
            csv.Should().Contain("A1");
            csv.Should().Contain("1234567890"); // FULL account number in the exported file (BR-2)
        }

        // The mask helper itself is the load-bearing BR-2 logic.
        PayrollReportService.MaskAccount("1234567890").Should().Be("******7890");
    }

    // ── US-RPT-003 AC-4/FR-6/NFR-3: bank-advice FULL (reveal) requires Payroll.ViewSensitive,
    //    returns UNMASKED accounts, and writes a "PayrollReport.ViewSensitive" audit row ──

    [Fact]
    public async Task BankAdvice_Full_ReturnsUnmaskedAccounts_AndWritesAuditRow()
    {
        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized);
        await SeedFinalizedSlip(_tenantA, runId, "A1", 50_000m, 5_000m,
            bankAccountNumber: "1234567890", bankName: "Acme Bank", bankBranchCode: "BR001");

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var reveal = await svc.RevealBankAdviceAsync(new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });

            reveal.IsSuccess.Should().BeTrue();
            reveal.Value!.Masked.Should().BeFalse();
            var line = reveal.Value.Lines.Single(l => l.EmployeeNo == "A1");
            line.AccountNumber.Should().Be("1234567890"); // FULL (unmasked) account number on the reveal path

            // NFR-3: every reveal writes an audit row with action EXACTLY "PayrollReport.ViewSensitive".
            var audit = db.AuditLogs.IgnoreQueryFilters()
                .Where(a => a.TenantId == _tenantA && a.Action == "PayrollReport.ViewSensitive")
                .ToList();
            audit.Should().ContainSingle();
            audit[0].ResourceType.Should().Be("PayrollReport");
        }
    }

    // ── US-RPT-003 AC-1/FR-3: the PayrollSummary report carries an embedded `summary` block with the
    //    4 KPI metrics + month-over-month variance (two consecutive months → correct variance) ──

    [Fact]
    public async Task PayrollSummary_CarriesSummaryBlock_WithFourMetrics_AndMoMVariance()
    {
        // April (previous): one slip, gross 50000, pf 5000 => net 45000.
        // May (current): two slips, gross 50000+30000=80000, pf 5000+3000=8000 => net 72000, 2 employees.
        var aprRun = BaseEntity.NewUuidV7();
        var mayRun = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, aprRun, PayrollRunStatus.Finalized, month: 4, year: 2026);
        await SeedRun(_tenantA, mayRun, PayrollRunStatus.Finalized, month: 5, year: 2026);
        await SeedFinalizedSlip(_tenantA, aprRun, "P1", 50_000m, 5_000m, month: 4, year: 2026);
        await SeedFinalizedSlip(_tenantA, mayRun, "C1", 50_000m, 5_000m, month: 5, year: 2026);
        await SeedFinalizedSlip(_tenantA, mayRun, "C2", 30_000m, 3_000m, month: 5, year: 2026);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(
                PayrollReportType.PayrollSummary, new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });

            result.IsSuccess.Should().BeTrue();
            var summary = result.Value!.Summary;
            summary.Should().NotBeNull();
            summary!.CurrentLabel.Should().Be("May 2026");
            summary.PreviousLabel.Should().Be("April 2026");
            summary.Metrics.Should().HaveCount(4);
            summary.Metrics.Select(m => m.Key).Should().Equal("gross", "deductions", "net", "employeeCount");

            // Totals == sum of the period's payslips (current = May).
            var gross = summary.Metrics.Single(m => m.Key == "gross");
            gross.IsCost.Should().BeTrue();
            gross.Current.Should().Be(80_000m);
            gross.Previous.Should().Be(50_000m);
            gross.Variance.Should().Be(30_000m); // 80000 - 50000

            var deductions = summary.Metrics.Single(m => m.Key == "deductions");
            deductions.Current.Should().Be(8_000m);   // 5000 + 3000
            deductions.Previous.Should().Be(5_000m);
            deductions.Variance.Should().Be(3_000m);

            var net = summary.Metrics.Single(m => m.Key == "net");
            net.Current.Should().Be(72_000m);          // 45000 + 27000
            net.Previous.Should().Be(45_000m);
            net.Variance.Should().Be(27_000m);

            var count = summary.Metrics.Single(m => m.Key == "employeeCount");
            count.IsCost.Should().BeFalse();
            count.Current.Should().Be(2m);
            count.Previous.Should().Be(1m);
            count.Variance.Should().Be(1m);
        }
    }

    // ── US-RPT-003 AC-1: no prior finalized period → previousLabel + previous/variance are null ──

    [Fact]
    public async Task PayrollSummary_SummaryBlock_NullPrevious_WhenNoPriorPeriod()
    {
        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized);
        await SeedFinalizedSlip(_tenantA, runId, "A1", 50_000m, 5_000m);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(
                PayrollReportType.PayrollSummary, new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026 });

            result.IsSuccess.Should().BeTrue();
            var summary = result.Value!.Summary;
            summary.Should().NotBeNull();
            summary!.PreviousLabel.Should().BeNull();
            summary.Metrics.Should().OnlyContain(m => m.Previous == null && m.Variance == null);
        }
    }

    // ── US-RPT-003: the report catalog still returns the 8 original report types (no run-summary type) ──

    [Fact]
    public void ListReportTypes_ReturnsEightTypes_WithoutRunSummary()
    {
        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var types = svc.ListReportTypes();
            types.Should().HaveCount(8);
            types.Select(t => t.Id).Should().BeEquivalentTo(
            [
                "PayrollSummary", "EmployeeRegister", "DepartmentSummary", "StatutoryDeduction",
                "BankAdvice", "Ctc", "Variance", "YearEndTaxStatement",
            ]);
            types.Select(t => t.Id).Should().NotContain("PayrollRunSummary");
        }
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

    // ── ISSUE-178 PR1: SalaryStructureId filter ─────────────────────────────
    //
    // Two employees on DIFFERENT currently-effective salary structures → a report filtered to structure A
    // returns only A's employee and excludes B's.

    private async Task SeedCurrentSalaryComponent(Guid tenantId, Guid employeeId, Guid salaryStructureId)
    {
        using var db = Db(tenantId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, EmployeeId = employeeId,
            SalaryStructureId = salaryStructureId, SalaryComponentId = BaseEntity.NewUuidV7(),
            AnnualAmount = 600_000m, MonthlyAmount = 50_000m,
            EffectiveFrom = today.AddMonths(-6), EffectiveTo = null, // currently effective
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SalaryStructureFilter_ReturnsOnlyEmployeesOnThatStructure()
    {
        var structureA = BaseEntity.NewUuidV7();
        var structureB = BaseEntity.NewUuidV7();
        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized);
        var a = await SeedFinalizedSlip(_tenantA, runId, "STRUCT-A", 50_000m, 5_000m);
        var b = await SeedFinalizedSlip(_tenantA, runId, "STRUCT-B", 30_000m, 3_000m);
        await SeedCurrentSalaryComponent(_tenantA, a.EmployeeId, structureA);
        await SeedCurrentSalaryComponent(_tenantA, b.EmployeeId, structureB);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(
                PayrollReportType.EmployeeRegister,
                new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026, SalaryStructureId = structureA });

            result.IsSuccess.Should().BeTrue();
            var empNos = result.Value!.Rows.Select(r => r.Cells[0]).ToList();
            empNos.Should().Contain("STRUCT-A");
            empNos.Should().NotContain("STRUCT-B"); // ISSUE-178: excluded — on a different salary structure
        }
    }

    // BUG-003 class: the SalaryStructureId subquery must be tenant-scoped — a different tenant's employee on the
    // SAME structure id must never leak into this tenant's filtered report (guards a future IgnoreQueryFilters regression).
    [Fact]
    public async Task SalaryStructureFilter_DoesNotLeakAcrossTenants()
    {
        var sharedStructure = BaseEntity.NewUuidV7(); // the SAME structure id exists in both tenants
        var runA = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runA, PayrollRunStatus.Finalized);
        var a = await SeedFinalizedSlip(_tenantA, runA, "A-EMP", 50_000m, 5_000m);
        await SeedCurrentSalaryComponent(_tenantA, a.EmployeeId, sharedStructure);

        var runB = BaseEntity.NewUuidV7();
        await SeedRun(_tenantB, runB, PayrollRunStatus.Finalized);
        var b = await SeedFinalizedSlip(_tenantB, runB, "B-EMP", 40_000m, 4_000m);
        await SeedCurrentSalaryComponent(_tenantB, b.EmployeeId, sharedStructure);

        var (db, _, svc) = Scope(_tenantA); // report runs in TENANT A's context
        using (db)
        {
            var result = await svc.GenerateReportAsync(
                PayrollReportType.EmployeeRegister,
                new PayrollReportQueryParams { PayMonth = 5, PayYear = 2026, SalaryStructureId = sharedStructure });

            result.IsSuccess.Should().BeTrue();
            var empNos = result.Value!.Rows.Select(r => r.Cells[0]).ToList();
            empNos.Should().Contain("A-EMP");
            empNos.Should().NotContain("B-EMP"); // cross-tenant isolation: tenant B's employee never leaks
        }
    }

    // ── ISSUE-178 PR1: DateFrom/DateTo pay-period range ─────────────────────
    //
    // Finalized slips across 3 pay-periods (Jan/Feb/Mar 2026). A report with DateFrom=2026-02-01 /
    // DateTo=2026-03-31 includes only the Feb + Mar slips and excludes Jan (filter is by pay-period year-month).

    [Fact]
    public async Task DateRangeFilter_IncludesOnlyPayPeriodsWithinRange()
    {
        var janRun = BaseEntity.NewUuidV7();
        var febRun = BaseEntity.NewUuidV7();
        var marRun = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, janRun, PayrollRunStatus.Finalized, month: 1, year: 2026);
        await SeedRun(_tenantA, febRun, PayrollRunStatus.Finalized, month: 2, year: 2026);
        await SeedRun(_tenantA, marRun, PayrollRunStatus.Finalized, month: 3, year: 2026);
        await SeedFinalizedSlip(_tenantA, janRun, "JAN", 50_000m, 5_000m, month: 1, year: 2026);
        await SeedFinalizedSlip(_tenantA, febRun, "FEB", 50_000m, 5_000m, month: 2, year: 2026);
        await SeedFinalizedSlip(_tenantA, marRun, "MAR", 50_000m, 5_000m, month: 3, year: 2026);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(
                PayrollReportType.EmployeeRegister,
                new PayrollReportQueryParams
                {
                    DateFrom = new DateOnly(2026, 2, 1),
                    DateTo = new DateOnly(2026, 3, 31),
                });

            result.IsSuccess.Should().BeTrue();
            var empNos = result.Value!.Rows.Select(r => r.Cells[0]).ToList();
            empNos.Should().Contain("FEB");
            empNos.Should().Contain("MAR");
            empNos.Should().NotContain("JAN"); // ISSUE-178: Jan is before DateFrom's month → excluded
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  TAX-4 / FR-1f: Year-End Tax Statement — per-employee, per-country fiscal-year summary of the persisted
    //  TaxableIncome + IncomeTaxWithheld slip columns. Columns:
    //    Employee No, Employee, Country, Fiscal Year, Taxable Income, Income Tax Withheld.
    // ════════════════════════════════════════════════════════════════════════

    /// <summary>Adds an active IncomeTax StatutoryRule (only the country / FY / effective window are read by the
    /// year-end report — no tax slabs needed).</summary>
    private async Task SeedIncomeTaxRule(
        Guid tenantId, string countryCode, string fiscalYear, DateOnly from, DateOnly to)
    {
        using var db = Db(tenantId);
        db.StatutoryRules.Add(new StatutoryRule
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId,
            RuleType = StatutoryRuleType.IncomeTax, RuleName = $"PAYE-{countryCode}",
            CountryCode = countryCode, FiscalYear = fiscalYear,
            EffectiveFrom = from, EffectiveTo = to, IsActive = true,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedLocationWithCountry(Guid tenantId, string countryCode)
    {
        using var db = Db(tenantId);
        var id = BaseEntity.NewUuidV7();
        db.Locations.Add(new Location
        {
            Id = id, TenantId = tenantId, Name = $"Branch-{countryCode}-{id:N}",
            TimeZone = "Asia/Colombo", CountryCode = countryCode, IsActive = true, IsDeleted = false,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task SeedTenantDefaultCountry(Guid tenantId, string? defaultCountryCode)
    {
        using var db = Db(tenantId);
        db.Tenants.Add(new Tenant
        {
            Id = tenantId, Subdomain = $"t{tenantId:N}"[..20], Name = "Test Co",
            DefaultCountryCode = defaultCountryCode,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedTaxEmployee(Guid tenantId, string no, Guid? locationId)
    {
        using var db = Db(tenantId);
        var empId = BaseEntity.NewUuidV7();
        db.Employees.Add(new Employee
        {
            Id = empId, TenantId = tenantId, EmployeeNo = no, FirstName = no, LastName = "X",
            Email = $"{no}@t.com", DateOfJoining = new DateTime(2020, 1, 1),
            EmploymentType = EmploymentType.FullTime, Status = EmployeeStatus.Active, IsActive = true,
            DepartmentId = BaseEntity.NewUuidV7(), JobTitleId = BaseEntity.NewUuidV7(), LocationId = locationId,
        });
        await db.SaveChangesAsync();
        return empId;
    }

    /// <summary>Adds a FINALIZED-run slip carrying the TAX-3 TaxableIncome + IncomeTaxWithheld columns.
    /// GrossEarnings/TotalDeductions are set DIFFERENT from Taxable/Withheld (gross &gt; taxable, deductions &gt;
    /// withheld) so a builder that wrongly summed GrossEarnings/TotalDeductions instead of the tax columns would
    /// produce different figures and fail the money assertions.</summary>
    private async Task SeedTaxSlip(
        Guid tenantId, Guid runId, Guid empId, int month, int year, decimal taxable, decimal withheld)
    {
        using var db = Db(tenantId);
        var gross = taxable + 500_000m;      // deliberately ≠ taxable (exemptions/reliefs reduce gross → taxable).
        var totalDeductions = withheld + 40_000m; // deliberately ≠ withheld (EPF/other deductions ≠ income tax).
        db.PayrollSlips.Add(new PayrollSlip
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollRunId = runId, EmployeeId = empId,
            GrossEarnings = gross, TotalDeductions = totalDeductions, NetSalary = gross - totalDeductions,
            TaxableIncome = taxable, IncomeTaxWithheld = withheld,
            WorkingDays = 22, PaidDays = 22, LopDays = 0, PayMonth = month, PayYear = year,
        });
        await db.SaveChangesAsync();
    }

    private static PayrollReportQueryParams YearEndParams(int year) =>
        new() { PayMonth = 12, PayYear = year };

    // ── Sums the persisted slip columns across the FY; Country + Fiscal Year correct; TotalRow sums ──

    [Fact]
    public async Task YearEndTaxStatement_SumsTaxableAndWithheld_AcrossFiscalYear()
    {
        // LK fiscal year Apr 2026 – Mar 2027.
        await SeedIncomeTaxRule(_tenantA, "LK", "2026-2027",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        var lkLoc = await SeedLocationWithCountry(_tenantA, "LK");
        var emp = await SeedTaxEmployee(_tenantA, "LK1", lkLoc);

        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized, month: 12, year: 2026);
        // Three months inside the FY window: taxable 100k/120k/140k, withheld 5k/6k/7k.
        await SeedTaxSlip(_tenantA, runId, emp, 4, 2026, taxable: 100_000m, withheld: 5_000m);
        await SeedTaxSlip(_tenantA, runId, emp, 5, 2026, taxable: 120_000m, withheld: 6_000m);
        await SeedTaxSlip(_tenantA, runId, emp, 6, 2026, taxable: 140_000m, withheld: 7_000m);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(PayrollReportType.YearEndTaxStatement, YearEndParams(2026));

            result.IsSuccess.Should().BeTrue();
            var report = result.Value!;
            report.Rows.Should().ContainSingle(); // one row per employee.

            var row = report.Rows.Single();
            // Columns: Employee No, Employee, Country, Fiscal Year, Taxable Income, Income Tax Withheld.
            row.Cells[0].Should().Be("LK1");
            row.Cells[2].Should().Be("LK");
            row.Cells[3].Should().Be("2026-2027");
            row.Cells[4].Should().Be("360000.00"); // 100k + 120k + 140k
            row.Cells[5].Should().Be("18000.00");   // 5k + 6k + 7k

            report.TotalRow!.Cells[4].Should().Be("360000.00");
            report.TotalRow.Cells[5].Should().Be("18000.00");
        }
    }

    // ── Multi-country: two employees in different countries → each row shows their own country + FY + only their
    //    own slips' sums ──

    [Fact]
    public async Task YearEndTaxStatement_MultiCountry_EachEmployeeUnderTheirOwnCountryAndFy()
    {
        await SeedIncomeTaxRule(_tenantA, "LK", "2026-2027",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        await SeedIncomeTaxRule(_tenantA, "IN", "FY26-27",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        var lkLoc = await SeedLocationWithCountry(_tenantA, "LK");
        var inLoc = await SeedLocationWithCountry(_tenantA, "IN");
        var lkEmp = await SeedTaxEmployee(_tenantA, "LK1", lkLoc);
        var inEmp = await SeedTaxEmployee(_tenantA, "IN1", inLoc);

        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized, month: 12, year: 2026);
        await SeedTaxSlip(_tenantA, runId, lkEmp, 4, 2026, taxable: 200_000m, withheld: 10_000m);
        await SeedTaxSlip(_tenantA, runId, lkEmp, 5, 2026, taxable: 200_000m, withheld: 10_000m);
        await SeedTaxSlip(_tenantA, runId, inEmp, 4, 2026, taxable: 300_000m, withheld: 30_000m);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(PayrollReportType.YearEndTaxStatement, YearEndParams(2026));

            result.IsSuccess.Should().BeTrue();
            var rows = result.Value!.Rows;
            rows.Should().HaveCount(2);

            var lk = rows.Single(r => r.Cells[0] == "LK1");
            lk.Cells[2].Should().Be("LK");
            lk.Cells[3].Should().Be("2026-2027");
            lk.Cells[4].Should().Be("400000.00"); // only LK1's own slips (200k + 200k)
            lk.Cells[5].Should().Be("20000.00");

            var ind = rows.Single(r => r.Cells[0] == "IN1");
            ind.Cells[2].Should().Be("IN");
            ind.Cells[3].Should().Be("FY26-27");
            ind.Cells[4].Should().Be("300000.00");
            ind.Cells[5].Should().Be("30000.00");

            // TotalRow sums ACROSS the two employees (a copy-first-row / mis-sum bug is caught only with >1 row).
            result.Value!.TotalRow!.Cells[4].Should().Be("700000.00"); // 400k + 300k.
            result.Value.TotalRow.Cells[5].Should().Be("50000.00");    // 20k + 30k.
        }
    }

    // ── FY-window: a slip OUTSIDE the FY (a prior-FY month) is NOT included in the sum ──

    [Fact]
    public async Task YearEndTaxStatement_ExcludesSlipsOutsideFiscalYearWindow()
    {
        await SeedIncomeTaxRule(_tenantA, "LK", "2026-2027",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        var lkLoc = await SeedLocationWithCountry(_tenantA, "LK");
        var emp = await SeedTaxEmployee(_tenantA, "LK1", lkLoc);

        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized, month: 12, year: 2026);
        // Prior FY (March 2026) — must be EXCLUDED (before EffectiveFrom) even though within the 3-year load window.
        await SeedTaxSlip(_tenantA, runId, emp, 3, 2026, taxable: 999_000m, withheld: 99_000m);
        // Next FY (April 2027) — must be EXCLUDED (after EffectiveTo); exercises the UPPER window boundary.
        await SeedTaxSlip(_tenantA, runId, emp, 4, 2027, taxable: 888_000m, withheld: 88_000m);
        // Inside the FY.
        await SeedTaxSlip(_tenantA, runId, emp, 4, 2026, taxable: 100_000m, withheld: 5_000m);
        await SeedTaxSlip(_tenantA, runId, emp, 5, 2026, taxable: 120_000m, withheld: 6_000m);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(PayrollReportType.YearEndTaxStatement, YearEndParams(2026));

            result.IsSuccess.Should().BeTrue();
            var row = result.Value!.Rows.Single();
            row.Cells[4].Should().Be("220000.00"); // 100k + 120k only — the Mar-2026 (prior FY) + Apr-2027 (next FY) excluded.
            row.Cells[5].Should().Be("11000.00");   // 5k + 6k only.
        }
    }

    // ── No-country employee in a MULTI-country tenant (no location, no tenant default) → EXCLUDED (not a 0-row) ──

    [Fact]
    public async Task YearEndTaxStatement_NoCountryEmployee_IsExcluded()
    {
        // Two countries' rules → no single-country fallback → a country-less employee is genuinely ambiguous.
        await SeedIncomeTaxRule(_tenantA, "LK", "2026-2027",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        await SeedIncomeTaxRule(_tenantA, "IN", "FY26-27",
            new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        // No tenant default country row seeded → tenant default is null.

        var lkLoc = await SeedLocationWithCountry(_tenantA, "LK");
        var lkEmp = await SeedTaxEmployee(_tenantA, "LK1", lkLoc);
        var noCountryEmp = await SeedTaxEmployee(_tenantA, "NC1", locationId: null); // no location.

        var runId = BaseEntity.NewUuidV7();
        await SeedRun(_tenantA, runId, PayrollRunStatus.Finalized, month: 12, year: 2026);
        await SeedTaxSlip(_tenantA, runId, lkEmp, 4, 2026, taxable: 100_000m, withheld: 5_000m);
        await SeedTaxSlip(_tenantA, runId, noCountryEmp, 4, 2026, taxable: 500_000m, withheld: 50_000m);

        var (db, _, svc) = Scope(_tenantA);
        using (db)
        {
            var result = await svc.GenerateReportAsync(PayrollReportType.YearEndTaxStatement, YearEndParams(2026));

            result.IsSuccess.Should().BeTrue();
            var empNos = result.Value!.Rows.Select(r => r.Cells[0]).ToList();
            empNos.Should().ContainSingle().Which.Should().Be("LK1");
            empNos.Should().NotContain("NC1"); // country-less → skipped, never fabricated as a 0-row.
        }
    }

    // ── ISSUE-176: the Statutory report's YTD column uses the per-country income-tax FISCAL year ──

    /// <summary>Adds a finalized-run slip + one Statutory (Provident Fund) detail line for an EXISTING employee.</summary>
    private async Task SeedStatutorySlip(Guid tenantId, Guid runId, Guid empId, int month, int year, decimal pf)
    {
        using var db = Db(tenantId);
        var slipId = BaseEntity.NewUuidV7();
        db.PayrollSlips.Add(new PayrollSlip
        {
            Id = slipId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = empId,
            GrossEarnings = 100_000m, TotalDeductions = pf, NetSalary = 100_000m - pf,
            WorkingDays = 22, PaidDays = 22, LopDays = 0, PayMonth = month, PayYear = year,
        });
        db.PayrollSlipDetails.Add(new PayrollSlipDetail
        {
            Id = BaseEntity.NewUuidV7(), TenantId = tenantId, PayrollSlipId = slipId,
            SalaryComponentId = BaseEntity.NewUuidV7(), ComponentName = "Provident Fund",
            ComponentType = nameof(SalaryComponentType.Statutory), Amount = pf,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>One finalized run + one statutory PF slip for an employee/period (each month gets its own run).</summary>
    private async Task SeedFinalizedStatutorySlip(Guid tenantId, Guid empId, int month, int year, decimal pf)
    {
        var runId = BaseEntity.NewUuidV7();
        await SeedRun(tenantId, runId, PayrollRunStatus.Finalized, month, year);
        await SeedStatutorySlip(tenantId, runId, empId, month, year, pf);
    }

    private async Task<PayrollReportResult> StatutoryReport(Guid tenantId, int month, int year)
    {
        var (db, _, svc) = Scope(tenantId);
        using (db)
        {
            var result = await svc.GenerateReportAsync(
                PayrollReportType.StatutoryDeduction,
                new PayrollReportQueryParams { PayMonth = month, PayYear = year });
            result.IsSuccess.Should().BeTrue(result.Error);
            return result.Value!;
        }
    }

    // Columns: Statutory Component, Employee Count, Monthly Total, Year-to-Date Total. Cells[2]=Monthly, Cells[3]=YTD.
    private static string Ytd(PayrollReportResult r) => r.Rows.Single(x => x.Cells[0] == "Provident Fund").Cells[3];
    private static string Monthly(PayrollReportResult r) => r.Rows.Single(x => x.Cells[0] == "Provident Fund").Cells[2];

    /// <summary>THE core ISSUE-176 arm: YTD spans the income-tax fiscal year [Apr..selected], excluding both the
    /// PRIOR fiscal year AND in-FY months AFTER the selected one. Kills the calendar mutant, the
    /// collapse-to-selected-month mutant (lower bound), and the no-upper-cap mutant simultaneously.</summary>
    [Fact]
    [Trait("Issue", "ISSUE-176")]
    public async Task StatutoryReport_Ytd_SpansFiscalYear_ExcludesPriorFyAndFutureMonths()
    {
        await SeedIncomeTaxRule(_tenantA, "LK", "2026-2027", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        var emp = await SeedTaxEmployee(_tenantA, "LK1", await SeedLocationWithCountry(_tenantA, "LK"));
        await SeedFinalizedStatutorySlip(_tenantA, emp, month: 2, year: 2026, pf: 500m);   // prior FY → OUT
        await SeedFinalizedStatutorySlip(_tenantA, emp, month: 4, year: 2026, pf: 1_000m); // in-FY, before selected → IN
        await SeedFinalizedStatutorySlip(_tenantA, emp, month: 5, year: 2026, pf: 2_000m); // selected month → IN
        await SeedFinalizedStatutorySlip(_tenantA, emp, month: 6, year: 2026, pf: 4_000m); // in-FY, AFTER selected → OUT

        var report = await StatutoryReport(_tenantA, month: 5, year: 2026);

        Monthly(report).Should().Be("2000.00", "the selected month (May) alone");
        Ytd(report).Should().Be(
            "3000.00",
            "fiscal YTD = Apr(1000)+May(2000). Excludes Feb (prior FY) and June (in-FY but after May). Calendar "
            + "would give 3500 (Feb+Apr+May); collapse-to-selected would give 2000 (May only); no-cap gives 7000");
    }

    /// <summary>Guards the year-1 candidate load: an in-FY slip in the PRIOR calendar year (Apr 2026 for a
    /// Feb-2027 selection, FY 2026-2027) must be included. Deleting `|| PayYear == year - 1` fails this.</summary>
    [Fact]
    [Trait("Issue", "ISSUE-176")]
    public async Task StatutoryReport_Ytd_IncludesPriorCalendarYearSlipsInTheSameFiscalYear()
    {
        await SeedIncomeTaxRule(_tenantA, "LK", "2026-2027", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        var emp = await SeedTaxEmployee(_tenantA, "LK1", await SeedLocationWithCountry(_tenantA, "LK"));
        await SeedFinalizedStatutorySlip(_tenantA, emp, month: 4, year: 2026, pf: 1_000m); // PayYear 2026 = year-1
        await SeedFinalizedStatutorySlip(_tenantA, emp, month: 2, year: 2027, pf: 2_000m); // selected month

        var report = await StatutoryReport(_tenantA, month: 2, year: 2027);

        Monthly(report).Should().Be("2000.00", "Feb 2027 alone");
        Ytd(report).Should().Be("3000.00", "FY 2026-2027 YTD includes the Apr-2026 (prior calendar year) slip");
    }

    /// <summary>The money-silent-drop guard: an employee with NO resolvable tax country (no location, no tenant
    /// default, multi-country rules → no sole fallback) keeps the CALENDAR-year YTD — their EPF/ETF is never
    /// dropped. Changing the fallback to "exclude" removes the PF row entirely and fails.</summary>
    [Fact]
    [Trait("Issue", "ISSUE-176")]
    public async Task StatutoryReport_Ytd_CountryLessEmployee_KeepsCalendarWindow_NotDropped()
    {
        await SeedIncomeTaxRule(_tenantA, "LK", "2026-2027", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        await SeedIncomeTaxRule(_tenantA, "IN", "FY26-27", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        var emp = await SeedTaxEmployee(_tenantA, "NC1", locationId: null); // no country resolves (multi-country, no default).
        await SeedFinalizedStatutorySlip(_tenantA, emp, month: 3, year: 2026, pf: 1_000m);
        await SeedFinalizedStatutorySlip(_tenantA, emp, month: 5, year: 2026, pf: 2_000m);

        var report = await StatutoryReport(_tenantA, month: 5, year: 2026);

        Ytd(report).Should().Be(
            "3000.00", "no tax country → calendar YTD (Jan..May) = 1000+2000; the country-less EPF must NOT be dropped");
    }

    /// <summary>Regression: a single-country tenant whose income-tax rule is calendar-aligned (Jan–Dec) gets a
    /// YTD identical to the old flat calendar behaviour — the fiscal window collapses correctly.</summary>
    [Fact]
    [Trait("Issue", "ISSUE-176")]
    public async Task StatutoryReport_Ytd_CalendarAlignedTenant_IsUnchanged()
    {
        await SeedIncomeTaxRule(_tenantA, "LK", "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        var emp = await SeedTaxEmployee(_tenantA, "LK1", await SeedLocationWithCountry(_tenantA, "LK"));
        await SeedFinalizedStatutorySlip(_tenantA, emp, month: 3, year: 2026, pf: 1_000m);
        await SeedFinalizedStatutorySlip(_tenantA, emp, month: 5, year: 2026, pf: 2_000m);

        var report = await StatutoryReport(_tenantA, month: 5, year: 2026);

        Ytd(report).Should().Be("3000.00", "a Jan–Dec fiscal year == the calendar year → YTD unchanged (Mar+May)");
    }
}
