using System.Globalization;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Payroll reports and analytics (US-PAY-009). Pure read/aggregation over the existing
/// payroll_slip / payroll_slip_detail / payroll_adjustment data from FINALIZED runs only (BR-1).
/// No new entity / migration — every figure is computed on-the-fly from the persisted slips (the
/// pre-aggregated materialized table in §7 is a documented perf follow-up).
///
/// <para>Tenant isolation (AC-5, FR-8): every query runs under the EF global query filter
/// (TenantId == ITenantContext.TenantId). Slips/details/runs/employees are all BaseEntity, so a
/// cross-tenant row is simply invisible — no extra WHERE is needed.</para>
///
/// <para>BR-7: terminated employees are NOT excluded — a slip exists for every period the employee was
/// active and paid, so reading from the persisted slips naturally includes them for those periods.</para>
///
/// <para>Component classification: payroll_slip_detail stores the denormalized component type
/// ("Earning" / "Deduction" / "Statutory" / "Reimbursement", US-PAY-003). We split gross vs deductions
/// on that string, mirroring the renderer's IsDeductionSide convention (Deduction + Statutory = deductions;
/// Earning + Reimbursement = gross).</para>
/// </summary>
public sealed class PayrollReportService : IPayrollReportService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IPayrollAuditLogger _auditLogger;
    private readonly ILogger<PayrollReportService> _logger;

    private const int TrendMonths = 12;

    public PayrollReportService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IPayrollAuditLogger auditLogger,
        ILogger<PayrollReportService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Report catalog
    // ══════════════════════════════════════════════════════════════

    public IReadOnlyList<PayrollReportDescriptorDto> ListReportTypes() =>
    [
        Descriptor(PayrollReportType.PayrollSummary, "Payroll Summary",
            "Period totals with a department-wise breakdown (basic, allowances, gross, statutory, deductions, net)."),
        Descriptor(PayrollReportType.EmployeeRegister, "Employee Payroll Register",
            "All employees with their component-wise breakdown for the period."),
        Descriptor(PayrollReportType.DepartmentSummary, "Department-wise Summary",
            "Department-level payroll totals for the period."),
        Descriptor(PayrollReportType.StatutoryDeduction, "Statutory Deduction Report",
            "Tax / EPF / ETF and other statutory deduction totals for filing."),
        Descriptor(PayrollReportType.BankAdvice, "Bank Advice File",
            "Salary disbursement file (employee, bank, account, net amount, narration)."),
        Descriptor(PayrollReportType.Ctc, "CTC Report",
            "Current cost-to-company of all employees."),
        Descriptor(PayrollReportType.Variance, "Payroll Variance Report",
            "Month-over-month change per employee; changes greater than 10% are flagged."),
        Descriptor(PayrollReportType.YearEndTaxStatement, "Year-End Tax Statements",
            "Per-employee, per-country fiscal-year summary of taxable income + income tax withheld "
          + "(from the finalized-slip TaxableIncome / IncomeTaxWithheld columns). One row per employee."),
    ];

    private static PayrollReportDescriptorDto Descriptor(
        PayrollReportType type, string name, string description, bool deferred = false) =>
        new() { Id = type.ToString(), Name = name, Description = description, Deferred = deferred };

    // ══════════════════════════════════════════════════════════════
    //  Public API
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PayrollReportResult>> GenerateReportAsync(
        PayrollReportType reportType, PayrollReportQueryParams qp, CancellationToken ct = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollReportResult>.Failure("Tenant context is not resolved.", 400);

        var (month, year, periodError) = await ResolvePeriodAsync(qp, ct);
        if (periodError is not null)
            return Result<PayrollReportResult>.Failure(periodError, 404);

        return reportType switch
        {
            PayrollReportType.PayrollSummary => await BuildPayrollSummaryAsync(month, year, qp, includeSummaryBlock: true, ct),
            PayrollReportType.DepartmentSummary => await BuildPayrollSummaryAsync(month, year, qp, includeSummaryBlock: false, ct),
            PayrollReportType.EmployeeRegister => await BuildEmployeeRegisterAsync(month, year, qp, ct),
            PayrollReportType.StatutoryDeduction => await BuildStatutoryReportAsync(month, year, qp, ct),
            PayrollReportType.Ctc => await BuildCtcReportAsync(qp, month, year, ct),
            PayrollReportType.Variance => await BuildVarianceReportAsync(month, year, qp, ct),
            PayrollReportType.BankAdvice => await BuildBankAdviceReportAsync(month, year, qp, masked: true, ct),
            PayrollReportType.YearEndTaxStatement => await BuildYearEndTaxStatementAsync(year, qp, ct),
            _ => Result<PayrollReportResult>.Failure($"Unsupported report type '{reportType}'.", 400),
        };
    }

    public async Task<Result<BankAdvicePreviewDto>> GetBankAdvicePreviewAsync(
        PayrollReportQueryParams qp, CancellationToken ct = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BankAdvicePreviewDto>.Failure("Tenant context is not resolved.", 400);

        var (month, year, periodError) = await ResolvePeriodAsync(qp, ct);
        if (periodError is not null)
            return Result<BankAdvicePreviewDto>.Failure(periodError, 404);

        var lines = await BuildBankAdviceLinesAsync(month, year, qp, masked: true, ct);

        return Result<BankAdvicePreviewDto>.Success(new BankAdvicePreviewDto
        {
            PayMonth = month,
            PayYear = year,
            Lines = lines,
            EmployeeCount = lines.Count,
            TotalNetAmount = lines.Sum(l => l.NetAmount),
            Masked = true,
            Note = "Account numbers are masked (last 4 only). Use the reveal endpoint (requires Payroll.ViewSensitive) "
                 + "to view full numbers — every reveal is audited (PayrollReport.ViewSensitive).",
        });
    }

    public async Task<Result<BankAdvicePreviewDto>> RevealBankAdviceAsync(
        PayrollReportQueryParams qp, CancellationToken ct = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BankAdvicePreviewDto>.Failure("Tenant context is not resolved.", 400);

        var (month, year, periodError) = await ResolvePeriodAsync(qp, ct);
        if (periodError is not null)
            return Result<BankAdvicePreviewDto>.Failure(periodError, 404);

        // FULL (unmasked) account numbers — this is the sensitive-PII path. Audit BEFORE returning (NFR-3).
        var lines = await BuildBankAdviceLinesAsync(month, year, qp, masked: false, ct);

        await _auditLogger.LogAndSaveAsync(
            action: PayrollAuditAction.PayrollReportViewSensitive,
            resourceType: PayrollAuditAction.ResourceType.PayrollReport,
            resourceId: PayrollReportType.BankAdvice.ToString(),
            before: null,
            after: new { reportType = PayrollReportType.BankAdvice.ToString(), payMonth = month, payYear = year, employeeCount = lines.Count },
            cancellationToken: ct);

        return Result<BankAdvicePreviewDto>.Success(new BankAdvicePreviewDto
        {
            PayMonth = month,
            PayYear = year,
            Lines = lines,
            EmployeeCount = lines.Count,
            TotalNetAmount = lines.Sum(l => l.NetAmount),
            Masked = false,
            Note = "Full (unmasked) bank account numbers — this access was recorded in the audit log (PayrollReport.ViewSensitive).",
        });
    }

    /// <summary>
    /// US-RPT-003 AC-1/FR-3: builds the embedded Payroll Run Summary block — period TOTALS (gross, total
    /// deductions, net, employee count) with a month-over-month comparison vs the previous finalized period.
    /// Returned on the <see cref="PayrollReportResult.Summary"/> of the PayrollSummary report only. The four
    /// metric keys are EXACTLY gross / deductions / net / employeeCount (matching the FE contract); previous
    /// and variance are null when there is no prior finalized period.
    /// </summary>
    private async Task<PayrollReportSummaryDto> BuildRunSummaryBlockAsync(
        int month, int year, PayrollReportQueryParams qp, CancellationToken ct)
    {
        // Current-period totals.
        var current = await ComputePeriodTotalsAsync(month, year, qp, ct);

        // Previous finalized period (handles January roll-back). Compared only if it actually has finalized slips.
        int prevMonth = month == 1 ? 12 : month - 1;
        int prevYear = month == 1 ? year - 1 : year;
        var previousQp = qp with { PayMonth = prevMonth, PayYear = prevYear, PayrollRunId = null };
        var previous = await ComputePeriodTotalsAsync(prevMonth, prevYear, previousQp, ct);
        bool hasPrevious = previous.EmployeeCount > 0;

        var currency = await ResolveTenantCurrencyAsync(ct);

        var metrics = new List<PayrollSummaryMetricDto>
        {
            BuildMetric("gross", "Total Gross", current.Gross, previous.Gross, hasPrevious, isCost: true),
            BuildMetric("deductions", "Total Deductions", current.TotalDeductions, previous.TotalDeductions, hasPrevious, isCost: true),
            BuildMetric("net", "Total Net Pay", current.Net, previous.Net, hasPrevious, isCost: true),
            BuildMetric("employeeCount", "Employees", current.EmployeeCount, previous.EmployeeCount, hasPrevious, isCost: false),
        };

        return new PayrollReportSummaryDto
        {
            Currency = currency,
            CurrentLabel = $"{MonthName(month)} {year}",
            PreviousLabel = hasPrevious ? $"{MonthName(prevMonth)} {prevYear}" : null,
            Metrics = metrics,
        };
    }

    /// <summary>BR-2: the tenant's configured ISO-4217 currency code (Tenant.Currency). Tenant is the tenant
    /// root (not a BaseEntity), so it is not subject to the global query filter — scope by id explicitly.</summary>
    private async Task<string> ResolveTenantCurrencyAsync(CancellationToken ct)
    {
        var tenantId = _tenantContext.TenantId;
        var currency = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Currency)
            .FirstOrDefaultAsync(ct);
        return string.IsNullOrWhiteSpace(currency) ? "USD" : currency;
    }

    public async Task<Result<PayrollReportExportResult>> ExportReportAsync(
        PayrollReportType reportType, PayrollExportFormat format,
        PayrollReportQueryParams qp, CancellationToken ct = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollReportExportResult>.Failure("Tenant context is not resolved.", 400);

        // For BankAdvice, the EXPORTED file carries FULL account numbers (BR-2) — bypass the masked
        // GenerateReportAsync path and build the full-number rows directly.
        Result<PayrollReportResult> reportResult;
        if (reportType == PayrollReportType.BankAdvice)
        {
            var (month, year, periodError) = await ResolvePeriodAsync(qp, ct);
            if (periodError is not null)
                return Result<PayrollReportExportResult>.Failure(periodError, 404);
            reportResult = await BuildBankAdviceReportAsync(month, year, qp, masked: false, ct);
        }
        else
        {
            reportResult = await GenerateReportAsync(reportType, qp, ct);
        }

        if (reportResult.IsFailure)
            return Result<PayrollReportExportResult>.Failure(reportResult.Error!, reportResult.StatusCode ?? 400);

        var (content, fileName, contentType) = PayrollReportRenderer.Render(format, reportResult.Value!);

        return Result<PayrollReportExportResult>.Success(new PayrollReportExportResult
        {
            FileContent = content,
            FileName = fileName,
            ContentType = contentType,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Report builders
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// FR-1a / FR-1c: department-wise breakdown for a period + a grand-total row. Columns per §7:
    /// Department, Employee Count, Total Basic, Total Allowances, Total Gross, Total Statutory,
    /// Total Other Deductions, Total Net.
    /// </summary>
    private async Task<Result<PayrollReportResult>> BuildPayrollSummaryAsync(
        int month, int year, PayrollReportQueryParams qp, bool includeSummaryBlock, CancellationToken ct)
    {
        var columns = new List<string>
        {
            "Department", "Employee Count", "Total Basic", "Total Allowances",
            "Total Gross", "Total Statutory", "Total Other Deductions", "Total Net",
        };

        var slips = await ScopedSlipsForPeriodAsync(month, year, qp, ct);
        var (deptNameById, deptIdByEmployee) = await DepartmentLookupsAsync(slips, ct);
        var detailsBySlip = await DetailsBySlipAsync(slips, ct);

        // Aggregate per department.
        var byDept = new Dictionary<string, DeptAccumulator>();
        foreach (var slip in slips)
        {
            var deptId = deptIdByEmployee.GetValueOrDefault(slip.EmployeeId);
            var deptName = deptId is { } d ? deptNameById.GetValueOrDefault(d, "(Unassigned)") : "(Unassigned)";
            var acc = byDept.TryGetValue(deptName, out var existing) ? existing : new DeptAccumulator();

            var details = detailsBySlip.GetValueOrDefault(slip.Id, []);
            var comp = ComponentBreakdown.From(details);

            acc.EmployeeCount += 1;
            acc.Basic += comp.Basic;
            acc.Allowances += comp.Allowances;
            acc.Gross += slip.GrossEarnings;
            acc.Statutory += comp.Statutory;
            acc.OtherDeductions += comp.OtherDeductions;
            acc.Net += slip.NetSalary;
            byDept[deptName] = acc;
        }

        var rows = byDept
            .OrderByDescending(kv => kv.Value.Net)
            .Select(kv => new PayrollReportRow
            {
                Cells =
                [
                    kv.Key,
                    kv.Value.EmployeeCount.ToString(CultureInfo.InvariantCulture),
                    Money(kv.Value.Basic), Money(kv.Value.Allowances), Money(kv.Value.Gross),
                    Money(kv.Value.Statutory), Money(kv.Value.OtherDeductions), Money(kv.Value.Net),
                ],
            })
            .ToList();

        var totalRow = new PayrollReportRow
        {
            Cells =
            [
                "TOTAL",
                byDept.Sum(d => d.Value.EmployeeCount).ToString(CultureInfo.InvariantCulture),
                Money(byDept.Sum(d => d.Value.Basic)), Money(byDept.Sum(d => d.Value.Allowances)),
                Money(byDept.Sum(d => d.Value.Gross)), Money(byDept.Sum(d => d.Value.Statutory)),
                Money(byDept.Sum(d => d.Value.OtherDeductions)), Money(byDept.Sum(d => d.Value.Net)),
            ],
        };

        // US-RPT-003 AC-1/FR-3: the PayrollSummary report ADDITIONALLY carries the KPI + MoM summary block.
        // DepartmentSummary reuses this builder but does NOT embed it (FE treats it as optional).
        var summary = includeSummaryBlock
            ? await BuildRunSummaryBlockAsync(month, year, qp, ct)
            : null;

        return Result<PayrollReportResult>.Success(new PayrollReportResult
        {
            ReportType = PayrollReportType.PayrollSummary.ToString(),
            Title = $"Payroll Summary — {MonthName(month)} {year}",
            PayMonth = month, PayYear = year,
            Columns = columns, Rows = rows, TotalRow = totalRow, TotalCount = rows.Count,
            Summary = summary,
        });
    }

    /// <summary>
    /// US-RPT-003 AC-1: period TOTALS — gross, statutory deductions, voluntary (non-statutory) deductions,
    /// total deductions, net, and the employee count — across the period's finalized slips. Deductions are
    /// split on ComponentType: Statutory vs Deduction (voluntary). Reuses the existing slip query + detail
    /// load + ComponentBreakdown; does NOT re-query slips from scratch.
    /// </summary>
    private async Task<PeriodTotals> ComputePeriodTotalsAsync(
        int month, int year, PayrollReportQueryParams qp, CancellationToken ct)
    {
        var slips = await ScopedSlipsForPeriodAsync(month, year, qp, ct);
        if (slips.Count == 0)
            return new PeriodTotals();

        var detailsBySlip = await DetailsBySlipAsync(slips, ct);

        decimal gross = 0m, statutory = 0m, voluntary = 0m, net = 0m;
        foreach (var slip in slips)
        {
            var comp = ComponentBreakdown.From(detailsBySlip.GetValueOrDefault(slip.Id, []));
            gross += slip.GrossEarnings;
            statutory += comp.Statutory;
            voluntary += comp.OtherDeductions;
            net += slip.NetSalary;
        }

        return new PeriodTotals
        {
            EmployeeCount = slips.Count,
            Gross = gross,
            Statutory = statutory,
            Voluntary = voluntary,
            Net = net,
        };
    }

    /// <summary>
    /// US-RPT-003 AC-1/FR-3: one summary metric. <paramref name="variance"/> = current − previous; both
    /// previous and variance are null when there is no prior finalized period (FE hides the comparison).
    /// </summary>
    private static PayrollSummaryMetricDto BuildMetric(
        string key, string label, decimal current, decimal previous, bool hasPrevious, bool isCost) =>
        new()
        {
            Key = key,
            Label = label,
            Current = current,
            Previous = hasPrevious ? previous : null,
            Variance = hasPrevious ? current - previous : null,
            IsCost = isCost,
        };

    /// <summary>
    /// FR-1b: every employee with their component-wise breakdown for the period — one row per employee,
    /// one column per distinct component name found across the period's slips, plus Gross / Deductions / Net.
    /// </summary>
    private async Task<Result<PayrollReportResult>> BuildEmployeeRegisterAsync(
        int month, int year, PayrollReportQueryParams qp, CancellationToken ct)
    {
        var slips = await ScopedSlipsForPeriodAsync(month, year, qp, ct);
        var detailsBySlip = await DetailsBySlipAsync(slips, ct);
        var (deptNameById, deptIdByEmployee) = await DepartmentLookupsAsync(slips, ct);
        var empById = await EmployeesByIdAsync(slips, ct);

        // Stable component column set: distinct component names, in min processing order seen.
        var componentOrder = detailsBySlip.Values
            .SelectMany(d => d)
            .GroupBy(d => d.ComponentName)
            .Select(g => g.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var columns = new List<string> { "Employee No", "Employee", "Department" };
        columns.AddRange(componentOrder);
        columns.AddRange(["Gross", "Deductions", "Net"]);

        var rows = new List<PayrollReportRow>();
        foreach (var slip in slips.OrderBy(s => empById.TryGetValue(s.EmployeeId, out var e) ? e.EmployeeNo : string.Empty))
        {
            empById.TryGetValue(slip.EmployeeId, out var emp);
            var deptId = deptIdByEmployee.GetValueOrDefault(slip.EmployeeId);
            var deptName = deptId is { } d ? deptNameById.GetValueOrDefault(d, "(Unassigned)") : "(Unassigned)";

            var details = detailsBySlip.GetValueOrDefault(slip.Id, []);
            var amountByComponent = details
                .GroupBy(x => x.ComponentName)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

            var cells = new List<string>
            {
                emp?.EmployeeNo ?? string.Empty,
                emp is null ? string.Empty : $"{emp.FirstName} {emp.LastName}".Trim(),
                deptName,
            };
            foreach (var comp in componentOrder)
                cells.Add(amountByComponent.TryGetValue(comp, out var amt) ? Money(amt) : Money(0m));
            cells.Add(Money(slip.GrossEarnings));
            cells.Add(Money(slip.TotalDeductions));
            cells.Add(Money(slip.NetSalary));

            rows.Add(new PayrollReportRow { Cells = cells });
        }

        return Result<PayrollReportResult>.Success(new PayrollReportResult
        {
            ReportType = PayrollReportType.EmployeeRegister.ToString(),
            Title = $"Employee Payroll Register — {MonthName(month)} {year}",
            PayMonth = month, PayYear = year,
            Columns = columns, Rows = rows, TotalCount = rows.Count,
        });
    }

    /// <summary>
    /// FR-1d / US-RPT-003 AC-3: statutory deduction summary — one row per statutory component (e.g. Income
    /// Tax, EPF, ETF) with the employee count, the MONTHLY total for the selected period, AND the
    /// YEAR-TO-DATE cumulative total across the fiscal year up to and including the period (BR-5), plus a
    /// grand-total row. Print/export-friendly for submission to statutory authorities.
    ///
    /// <para>FISCAL-YEAR ASSUMPTION: no per-tenant fiscal-year-start config is modelled (BR-3 says it is a
    /// tenant config), so the fiscal year is taken as the CALENDAR year (January .. selected month) of the
    /// selected <paramref name="year"/>. When a fiscal-year-start config lands, change the YTD window's lower
    /// bound. The YTD column reuses <see cref="FinalizedSlipsQuery"/> + the same employee-side filters.</para>
    /// </summary>
    private async Task<Result<PayrollReportResult>> BuildStatutoryReportAsync(
        int month, int year, PayrollReportQueryParams qp, CancellationToken ct)
    {
        var columns = new List<string> { "Statutory Component", "Employee Count", "Monthly Total", "Year-to-Date Total" };

        // Monthly (selected-period) statutory lines.
        var slips = await ScopedSlipsForPeriodAsync(month, year, qp, ct);
        var detailsBySlip = await DetailsBySlipAsync(slips, ct);
        var monthlyByComponent = detailsBySlip.Values
            .SelectMany(d => d)
            .Where(d => IsStatutory(d.ComponentType))
            .GroupBy(d => d.ComponentName)
            .ToDictionary(
                g => g.Key,
                g => (Count: g.Select(x => x.PayrollSlipId).Distinct().Count(), Total: g.Sum(x => x.Amount)),
                StringComparer.OrdinalIgnoreCase);

        // Year-to-date (January .. selected month, same fiscal year) statutory lines.
        var ytdSlips = await ScopedSlipsForFiscalYtdAsync(month, year, qp, ct);
        var ytdDetailsBySlip = await DetailsBySlipAsync(ytdSlips, ct);
        var ytdByComponent = ytdDetailsBySlip.Values
            .SelectMany(d => d)
            .Where(d => IsStatutory(d.ComponentType))
            .GroupBy(d => d.ComponentName)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount), StringComparer.OrdinalIgnoreCase);

        // Union of component names (a component may appear in YTD but not the selected month, and vice-versa).
        var componentNames = monthlyByComponent.Keys
            .Union(ytdByComponent.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(name => ytdByComponent.GetValueOrDefault(name, 0m))
            .ToList();

        var rows = componentNames
            .Select(name =>
            {
                var monthly = monthlyByComponent.GetValueOrDefault(name);
                var ytd = ytdByComponent.GetValueOrDefault(name, 0m);
                return new PayrollReportRow
                {
                    Cells =
                    [
                        name,
                        monthly.Count.ToString(CultureInfo.InvariantCulture),
                        Money(monthly.Total),
                        Money(ytd),
                    ],
                };
            })
            .ToList();

        var totalRow = new PayrollReportRow
        {
            Cells =
            [
                "TOTAL", string.Empty,
                Money(monthlyByComponent.Values.Sum(v => v.Total)),
                Money(ytdByComponent.Values.Sum()),
            ],
        };

        return Result<PayrollReportResult>.Success(new PayrollReportResult
        {
            ReportType = PayrollReportType.StatutoryDeduction.ToString(),
            Title = $"Statutory Deduction Report — {MonthName(month)} {year}",
            PayMonth = month, PayYear = year,
            Columns = columns, Rows = rows, TotalRow = totalRow, TotalCount = rows.Count,
            Note = "Year-to-Date is cumulative from January to the selected month of the same calendar year "
                 + "(fiscal-year-start config is a documented follow-up, BR-3/BR-5).",
        });
    }

    /// <summary>
    /// US-RPT-003 AC-3/BR-5: the finalized slips from January to the selected month (inclusive) of the same
    /// fiscal (calendar) year, with the same employee-side filters applied. A specific PayrollRunId filter is
    /// intentionally NOT applied here (YTD spans every finalized run in the window).
    /// </summary>
    private async Task<List<PayrollSlip>> ScopedSlipsForFiscalYtdAsync(
        int month, int year, PayrollReportQueryParams qp, CancellationToken ct)
    {
        var slips = await FinalizedSlipsQuery()
            .Where(s => s.PayYear == year && s.PayMonth >= 1 && s.PayMonth <= month)
            .ToListAsync(ct);

        if (slips.Count == 0)
            return slips;

        bool hasEmployeeFilter = qp.DepartmentId is not null || qp.JobTitleId is not null
            || qp.LocationId is not null || qp.SalaryStructureId is not null
            || !string.IsNullOrWhiteSpace(qp.EmploymentType) || !string.IsNullOrWhiteSpace(qp.EmployeeSearch);
        if (!hasEmployeeFilter)
            return slips;

        var allowedEmployeeIds = (await ScopedEmployeesQuery(qp).Select(e => e.Id).ToListAsync(ct)).ToHashSet();
        return slips.Where(s => allowedEmployeeIds.Contains(s.EmployeeId)).ToList();
    }

    /// <summary>
    /// FR-1h / US-RPT-003 BR-6: current Cost-to-Company of all employees. CTC = the employee's gross
    /// (EARNING-side) annual pay PLUS EMPLOYER CONTRIBUTIONS (pension / insurance) on top of gross.
    /// Columns: Monthly Gross, Annual Gross, Employer Contributions (annual), Annual CTC (= annual gross +
    /// employer contributions).
    ///
    /// <para>EMPLOYER-CONTRIBUTION ASSUMPTION (documented per the story brief): no dedicated
    /// employer-contribution component type/flag is modelled — SalaryComponentType has only Earning /
    /// Deduction / Statutory / Reimbursement, and statutory items are employee-side. So the employer
    /// contribution is APPROXIMATED as a 1:1 employer match of the employee's CURRENT STATUTORY components
    /// (the common EPF/pension-matching convention). When a real employer-contribution component (or rate)
    /// lands, swap this estimate for the modelled value. The estimate is labelled "(est.)" so it is never
    /// mistaken for a configured figure.</para>
    ///
    /// The period filter does not gate this report (it is a "current" snapshot), but the resolved period is
    /// echoed for the file name.
    /// </summary>
    private async Task<Result<PayrollReportResult>> BuildCtcReportAsync(
        PayrollReportQueryParams qp, int month, int year, CancellationToken ct)
    {
        var columns = new List<string>
        {
            "Employee No", "Employee", "Department", "Monthly Gross", "Annual Gross",
            "Employer Contributions (est.)", "Annual CTC",
        };

        var employees = await ScopedEmployeesQuery(qp).ToListAsync(ct);
        if (employees.Count == 0)
        {
            return Result<PayrollReportResult>.Success(new PayrollReportResult
            {
                ReportType = PayrollReportType.Ctc.ToString(),
                Title = $"CTC Report — as of {DateTime.UtcNow:dd MMM yyyy}",
                PayMonth = month, PayYear = year, Columns = columns, Rows = [], TotalCount = 0,
            });
        }

        var employeeIds = employees.Select(e => e.Id).ToList();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Classify current components by their SalaryComponent.Type.
        var componentTypeById = (await _dbContext.SalaryComponents.AsNoTracking()
                .Select(c => new { c.Id, c.Type })
                .ToListAsync(ct))
            .ToDictionary(c => c.Id, c => c.Type);

        var rawComponents = await _dbContext.EmployeeSalaryComponents.AsNoTracking()
            .Where(c => employeeIds.Contains(c.EmployeeId)
                        && c.EffectiveFrom <= today
                        && (c.EffectiveTo == null || c.EffectiveTo >= today))
            .Select(c => new { c.EmployeeId, c.SalaryComponentId, c.AnnualAmount, c.MonthlyAmount })
            .ToListAsync(ct);

        // Earning-side gross.
        var grossByEmp = rawComponents
            .Where(c => componentTypeById.GetValueOrDefault(c.SalaryComponentId) == SalaryComponentType.Earning)
            .GroupBy(c => c.EmployeeId)
            .ToDictionary(g => g.Key, g => (Annual: g.Sum(x => x.AnnualAmount), Monthly: g.Sum(x => x.MonthlyAmount)));

        // BR-6: employer contributions, estimated as a 1:1 match of the employee's statutory components.
        var employerContribByEmp = rawComponents
            .Where(c => componentTypeById.GetValueOrDefault(c.SalaryComponentId) == SalaryComponentType.Statutory)
            .GroupBy(c => c.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.AnnualAmount));

        var deptNames = await DepartmentNamesAsync(employees, ct);

        var rows = employees
            .Select(e =>
            {
                var gross = grossByEmp.GetValueOrDefault(e.Id);
                var employer = employerContribByEmp.GetValueOrDefault(e.Id, 0m);
                var annualCtc = gross.Annual + employer;
                return new PayrollReportRow
                {
                    Cells =
                    [
                        e.EmployeeNo,
                        $"{e.FirstName} {e.LastName}".Trim(),
                        deptNames.GetValueOrDefault(e.DepartmentId, string.Empty),
                        Money(gross.Monthly), Money(gross.Annual),
                        Money(employer), Money(annualCtc),
                    ],
                };
            })
            .ToList();

        decimal totalMonthlyGross = grossByEmp.Sum(x => x.Value.Monthly);
        decimal totalAnnualGross = grossByEmp.Sum(x => x.Value.Annual);
        decimal totalEmployer = employerContribByEmp.Sum(x => x.Value);

        var totalRow = new PayrollReportRow
        {
            Cells =
            [
                "TOTAL", string.Empty, string.Empty,
                Money(totalMonthlyGross), Money(totalAnnualGross),
                Money(totalEmployer), Money(totalAnnualGross + totalEmployer),
            ],
        };

        return Result<PayrollReportResult>.Success(new PayrollReportResult
        {
            ReportType = PayrollReportType.Ctc.ToString(),
            Title = $"CTC Report — as of {DateTime.UtcNow:dd MMM yyyy}",
            PayMonth = month, PayYear = year,
            Columns = columns, Rows = rows, TotalRow = totalRow, TotalCount = rows.Count,
            Note = "Annual CTC = annual gross + employer contributions (BR-6). Employer contributions are "
                 + "ESTIMATED as a 1:1 match of statutory components (no employer-contribution component is "
                 + "modelled yet) — labelled '(est.)'.",
        });
    }

    /// <summary>
    /// FR-1g / BR-4: month-over-month net variance per employee. Compares the period's net against the
    /// previous finalized period's net; rows with a &gt;10% change (up or down) are flagged. New / dropped
    /// employees are shown with a blank baseline / current and flagged as appearing/leaving.
    /// </summary>
    private async Task<Result<PayrollReportResult>> BuildVarianceReportAsync(
        int month, int year, PayrollReportQueryParams qp, CancellationToken ct)
    {
        var columns = new List<string>
        {
            "Employee No", "Employee", "Department",
            "Previous Net", "Current Net", "Change", "Change %", "Flagged",
        };

        var current = await ScopedSlipsForPeriodAsync(month, year, qp, ct);

        // Previous period (handles January roll-back).
        int prevMonth = month == 1 ? 12 : month - 1;
        int prevYear = month == 1 ? year - 1 : year;
        var previous = await ScopedSlipsForPeriodAsync(prevMonth, prevYear, qp, ct);

        var currentByEmp = current.ToDictionary(s => s.EmployeeId, s => s.NetSalary);
        var previousByEmp = previous.ToDictionary(s => s.EmployeeId, s => s.NetSalary);

        var allSlips = current.Concat(previous).ToList();
        var (deptNameById, deptIdByEmployee) = await DepartmentLookupsAsync(allSlips, ct);
        var empById = await EmployeesByIdAsync(allSlips, ct);

        var employeeIds = currentByEmp.Keys.Union(previousByEmp.Keys).ToList();

        var rows = new List<PayrollReportRow>();
        foreach (var empId in employeeIds)
        {
            empById.TryGetValue(empId, out var emp);
            var deptId = deptIdByEmployee.GetValueOrDefault(empId);
            var deptName = deptId is { } d ? deptNameById.GetValueOrDefault(d, "(Unassigned)") : "(Unassigned)";

            decimal? prev = previousByEmp.TryGetValue(empId, out var p) ? p : null;
            decimal? curr = currentByEmp.TryGetValue(empId, out var c) ? c : null;

            decimal change = (curr ?? 0m) - (prev ?? 0m);
            decimal? changePct = prev is { } pv && pv != 0m
                ? Math.Round(change / pv * 100m, 2)
                : (prev is null ? null : (curr is null ? -100m : null));

            bool flagged = changePct is { } pct && Math.Abs(pct) > 10m
                           || prev is null || curr is null;

            rows.Add(new PayrollReportRow
            {
                Cells =
                [
                    emp?.EmployeeNo ?? string.Empty,
                    emp is null ? string.Empty : $"{emp.FirstName} {emp.LastName}".Trim(),
                    deptName,
                    prev is { } pp ? Money(pp) : "-",
                    curr is { } cc ? Money(cc) : "-",
                    Money(change),
                    changePct is { } cp ? cp.ToString("0.##", CultureInfo.InvariantCulture) + "%" : "-",
                    flagged ? "Yes" : "No",
                ],
            });
        }

        // Flagged rows first, then biggest absolute change.
        rows = rows
            .OrderByDescending(r => r.Cells[7] == "Yes")
            .ThenByDescending(r => Math.Abs(ParseMoney(r.Cells[5])))
            .ToList();

        return Result<PayrollReportResult>.Success(new PayrollReportResult
        {
            ReportType = PayrollReportType.Variance.ToString(),
            Title = $"Payroll Variance — {MonthName(month)} {year} vs {MonthName(prevMonth)} {prevYear}",
            PayMonth = month, PayYear = year,
            Columns = columns, Rows = rows, TotalCount = rows.Count,
            Note = "Rows with a net change greater than 10%, or where an employee newly appears/drops, are flagged (BR-4).",
        });
    }

    /// <summary>
    /// FR-1e / AC-2 / §7: bank advice file. Columns: Employee No, Employee Name, Bank Name, Branch Code,
    /// Account Number, Net Amount, Narration. Account numbers are masked (last 4 only) when
    /// <paramref name="masked"/> (BR-2 — JSON preview); FULL in the exported file.
    /// </summary>
    private async Task<Result<PayrollReportResult>> BuildBankAdviceReportAsync(
        int month, int year, PayrollReportQueryParams qp, bool masked, CancellationToken ct)
    {
        var columns = new List<string>
        {
            "Employee No", "Employee Name", "Bank Name", "Branch Code", "Account Number", "Net Amount", "Narration",
        };

        var lines = await BuildBankAdviceLinesAsync(month, year, qp, masked, ct);

        var rows = lines
            .Select(l => new PayrollReportRow
            {
                Cells =
                [
                    l.EmployeeNo, l.EmployeeName, l.BankName, l.BranchCode,
                    l.AccountNumber, Money(l.NetAmount), l.Narration,
                ],
            })
            .ToList();

        var totalRow = new PayrollReportRow
        {
            Cells = ["TOTAL", string.Empty, string.Empty, string.Empty, string.Empty, Money(lines.Sum(l => l.NetAmount)), string.Empty],
        };

        return Result<PayrollReportResult>.Success(new PayrollReportResult
        {
            ReportType = PayrollReportType.BankAdvice.ToString(),
            Title = $"Bank Advice — {MonthName(month)} {year}",
            PayMonth = month, PayYear = year,
            Columns = columns, Rows = rows, TotalRow = totalRow, TotalCount = rows.Count,
            Note = masked
                ? "Account numbers are masked (last 4 only); the exported file carries full numbers (BR-2)."
                : null,
        });
    }

    /// <summary>
    /// Builds the per-employee bank-advice lines for a finalized period (US-RPT-003 AC-4). Bank Name /
    /// Branch Code (IFSC/SWIFT) / Account Number are read from the Employee entity. The account number is
    /// MASKED (last-4 only, FR-6) when <paramref name="masked"/>; FULL only on the audited reveal/export
    /// path. Net amount + narration are derived from the slip.
    /// </summary>
    private async Task<IReadOnlyList<BankAdviceLineDto>> BuildBankAdviceLinesAsync(
        int month, int year, PayrollReportQueryParams qp, bool masked, CancellationToken ct)
    {
        var slips = await ScopedSlipsForPeriodAsync(month, year, qp, ct);
        var empById = await EmployeesByIdAsync(slips, ct);
        string narration = $"Salary {MonthName(month)} {year}";

        return slips
            .Select(slip =>
            {
                empById.TryGetValue(slip.EmployeeId, out var emp);
                string account = emp?.BankAccountNumber ?? string.Empty;
                return new BankAdviceLineDto
                {
                    EmployeeNo = emp?.EmployeeNo ?? string.Empty,
                    EmployeeName = emp is null ? string.Empty : $"{emp.FirstName} {emp.LastName}".Trim(),
                    BankName = emp?.BankName ?? string.Empty,
                    BranchCode = emp?.BankBranchCode ?? string.Empty,
                    AccountNumber = masked ? MaskAccount(account) : account,
                    NetAmount = slip.NetSalary,
                    Narration = narration,
                };
            })
            .OrderBy(l => l.EmployeeNo, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// FR-1f / AC-3 (US-PAY-009 TAX-4): per-employee, per-country FISCAL-YEAR summary of taxable income + income
    /// tax withheld. One row per employee, summing the persisted <see cref="PayrollSlip.TaxableIncome"/> +
    /// <see cref="PayrollSlip.IncomeTaxWithheld"/> (persisted on every finalized slip by TAX-3) across the FISCAL
    /// YEAR the employee's BRANCH-country runs.
    ///
    /// <para>Each employee is taxed under their BRANCH/Location's country (the SAME precedence the payroll run used
    /// — Location.CountryCode → Tenant.DefaultCountryCode → sole rule country → null), so the statement reconciles
    /// with what was actually withheld. Multi-country tenants get each employee under THEIR own country's fiscal
    /// year (from the in-effect IncomeTax rule's EffectiveFrom/EffectiveTo/FiscalYear). An employee with no
    /// resolvable tax country (only reachable in a genuinely multi-country tenant with no location/tenant country),
    /// or whose country has no in-effect IncomeTax rule, or who has no finalized slips inside their FY window, is
    /// SKIPPED (never fabricated).</para>
    ///
    /// <para>The representative period is (<paramref name="year"/>, PayMonth ?? 12) — December by default — so the
    /// FY containing that period is chosen (e.g. an LK Apr–Mar year, or a calendar Jan–Dec year). The per-employee
    /// PDF + bulk-ZIP (5,000-scale) is a follow-up beyond this columnar summary; large tenants already route through
    /// the async Hangfire export pipeline.</para>
    /// </summary>
    private async Task<Result<PayrollReportResult>> BuildYearEndTaxStatementAsync(
        int year, PayrollReportQueryParams qp, CancellationToken ct)
    {
        var columns = new List<string>
        {
            "Employee No", "Employee", "Country", "Fiscal Year", "Taxable Income", "Income Tax Withheld",
        };

        // Representative period: the year-end month (December) unless a period month is pinned, so the FY that
        // CONTAINS (year, month) is the one selected per country.
        int month = qp.PayMonth ?? 12;
        var periodDate = FiscalYearResolver.PeriodDate(year, month);
        var monthStart = periodDate;
        var monthEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // ── Per-country effective IncomeTax rule (mirror PayrollRunProcessor's country-filtered SelectEffective).
        // Group the active-for-period IncomeTax rules by normalized country and pick the in-effect version; keep
        // its (EffectiveFrom, EffectiveTo, FiscalYear) — the FY WINDOW the employee's country runs.
        var incomeTaxRuleRows = await _dbContext.StatutoryRules.AsNoTracking()
            .Where(r => r.IsActive
                && r.RuleType == StatutoryRuleType.IncomeTax
                && r.EffectiveFrom <= monthEnd
                && (r.EffectiveTo == null || r.EffectiveTo >= monthStart))
            .Select(r => new { r.CountryCode, r.EffectiveFrom, r.EffectiveTo, r.FiscalYear })
            .ToListAsync(ct);

        var fyByCountry = new Dictionary<string, (DateOnly From, DateOnly? To, string FiscalYear)>();
        foreach (var grp in incomeTaxRuleRows
            .Where(r => !string.IsNullOrWhiteSpace(r.CountryCode))
            .GroupBy(r => r.CountryCode!.Trim().ToUpperInvariant()))
        {
            var candidates = grp.ToList();
            var ranges = candidates.Select(r => (r.EffectiveFrom, r.EffectiveTo)).ToList();
            var idx = FiscalYearResolver.SelectEffective(periodDate, ranges);
            if (idx >= 0)
                fyByCountry[grp.Key] = (candidates[idx].EffectiveFrom, candidates[idx].EffectiveTo, candidates[idx].FiscalYear);
        }

        // ── Employee → country resolution inputs (mirror the run's batch loads; no N+1). ──
        var locationCountry = await _dbContext.Locations.AsNoTracking()
            .Where(l => l.CountryCode != null)
            .Select(l => new { l.Id, l.CountryCode })
            .ToDictionaryAsync(x => x.Id, x => x.CountryCode!, ct);

        var tenantDefaultRaw = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.DefaultCountryCode)
            .FirstOrDefaultAsync(ct);
        var tenantDefaultCountry = string.IsNullOrWhiteSpace(tenantDefaultRaw)
            ? null
            : tenantDefaultRaw.Trim().ToUpperInvariant();

        // Single-country backward-compat fallback: the distinct non-null country across ALL active-for-period
        // rules, when EXACTLY one (mirrors the run so a single-country tenant taxes country-less employees).
        var ruleCountriesRaw = await _dbContext.StatutoryRules.AsNoTracking()
            .Where(r => r.IsActive
                && r.EffectiveFrom <= monthEnd
                && (r.EffectiveTo == null || r.EffectiveTo >= monthStart))
            .Select(r => r.CountryCode)
            .Distinct()
            .ToListAsync(ct);
        var ruleCountries = ruleCountriesRaw
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
        var soleRuleCountry = ruleCountries.Count == 1 ? ruleCountries[0] : null;

        // ── Employees (respecting the FR-3 employee-side filters) + their finalized slips (batched ONCE). ──
        var employees = await ScopedEmployeesQuery(qp).ToListAsync(ct);
        if (employees.Count == 0)
            return EmptyYearEndResult(columns, month, year);

        var employeeIds = employees.Select(e => e.Id).ToHashSet();

        // Load finalized slips for the 3 pay-years that any resolved FY window can span (a period-year FY plus an
        // Apr–Mar-style FY that crosses the calendar boundary), then group per employee in memory (no N+1). The
        // per-employee FY window filter below trims to the exact months that belong in each employee's FY.
        var candidateYears = new[] { year - 1, year, year + 1 };
        var slipRows = await FinalizedSlipsQuery()
            .Where(s => candidateYears.Contains(s.PayYear))
            .Select(s => new { s.EmployeeId, s.PayYear, s.PayMonth, s.TaxableIncome, s.IncomeTaxWithheld })
            .ToListAsync(ct);

        var slipsByEmployee = slipRows
            .Where(s => employeeIds.Contains(s.EmployeeId))
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<PayrollReportRow>();
        foreach (var emp in employees)
        {
            // TAX-1 precedence: Location.CountryCode → Tenant.DefaultCountryCode → sole rule country → null.
            string? empCountry = null;
            if (emp.LocationId is { } locId && locationCountry.TryGetValue(locId, out var locCc))
                empCountry = locCc;
            empCountry ??= tenantDefaultCountry;
            empCountry = string.IsNullOrWhiteSpace(empCountry) ? null : empCountry.Trim().ToUpperInvariant();
            empCountry ??= soleRuleCountry;

            // No country, or a country with no in-effect IncomeTax rule → not taxed here → SKIP (never fabricate).
            if (empCountry is null || !fyByCountry.TryGetValue(empCountry, out var fy))
                continue;
            if (!slipsByEmployee.TryGetValue(emp.Id, out var empSlips))
                continue;

            decimal taxable = 0m, withheld = 0m;
            int inWindow = 0;
            foreach (var s in empSlips)
            {
                var period = new DateOnly(s.PayYear, s.PayMonth, 1);
                if (period >= fy.From && (fy.To is null || period <= fy.To.Value))
                {
                    taxable += s.TaxableIncome;
                    withheld += s.IncomeTaxWithheld;
                    inWindow++;
                }
            }
            if (inWindow == 0)
                continue; // no finalized slips inside this employee's FY window.

            rows.Add(new PayrollReportRow
            {
                Cells =
                [
                    emp.EmployeeNo,
                    $"{emp.FirstName} {emp.LastName}".Trim(),
                    empCountry,
                    fy.FiscalYear,
                    Money(taxable),
                    Money(withheld),
                ],
            });
        }

        rows = rows.OrderBy(r => r.Cells[0], StringComparer.OrdinalIgnoreCase).ToList();

        var totalRow = new PayrollReportRow
        {
            Cells =
            [
                "TOTAL", string.Empty, string.Empty, string.Empty,
                Money(rows.Sum(r => ParseMoney(r.Cells[4]))),
                Money(rows.Sum(r => ParseMoney(r.Cells[5]))),
            ],
        };

        return Result<PayrollReportResult>.Success(new PayrollReportResult
        {
            ReportType = PayrollReportType.YearEndTaxStatement.ToString(),
            Title = "Year-End Tax Statements",
            PayMonth = month, PayYear = year,
            Columns = columns, Rows = rows, TotalRow = totalRow, TotalCount = rows.Count,
            Note = "Per-employee fiscal-year summary. Each employee's Taxable Income + Income Tax Withheld are the "
                 + "SUM of their finalized-slip TaxableIncome / IncomeTaxWithheld within their BRANCH-country's "
                 + "fiscal-year window (Location.CountryCode → tenant default → sole rule country) — so multi-country "
                 + "tenants show each employee under their own country's fiscal year. Employees with no resolvable "
                 + "tax country, no in-effect income-tax rule, or no finalized slips in the window are omitted. A "
                 + "per-employee PDF + bulk-ZIP (5,000-scale) is a follow-up beyond this columnar summary.",
        });
    }

    private static Result<PayrollReportResult> EmptyYearEndResult(IReadOnlyList<string> columns, int month, int year) =>
        Result<PayrollReportResult>.Success(new PayrollReportResult
        {
            ReportType = PayrollReportType.YearEndTaxStatement.ToString(),
            Title = "Year-End Tax Statements",
            PayMonth = month, PayYear = year,
            Columns = columns, Rows = [], TotalCount = 0,
        });

    // ══════════════════════════════════════════════════════════════
    //  Analytics (FR-5)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<PayrollAnalyticsResult>> GetAnalyticsAsync(
        PayrollAnalyticsChartType chartType, PayrollReportQueryParams qp, CancellationToken ct = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PayrollAnalyticsResult>.Failure("Tenant context is not resolved.", 400);

        PayrollAnalyticsResult result = chartType switch
        {
            PayrollAnalyticsChartType.MonthlyTrend => await BuildMonthlyTrendAsync(qp, ct),
            PayrollAnalyticsChartType.DepartmentCostDistribution => await BuildDepartmentCostDistributionAsync(qp, ct),
            PayrollAnalyticsChartType.StatutoryBreakdown => await BuildStatutoryBreakdownAsync(qp, ct),
            _ => new PayrollAnalyticsResult { ChartType = chartType.ToString() },
        };

        return Result<PayrollAnalyticsResult>.Success(result);
    }

    /// <summary>Monthly gross / deductions / net over the last 12 finalized periods (line chart).</summary>
    private async Task<PayrollAnalyticsResult> BuildMonthlyTrendAsync(PayrollReportQueryParams qp, CancellationToken ct)
    {
        var (anchorMonth, anchorYear, _) = await ResolvePeriodAsync(qp, ct);
        if (anchorMonth == 0)
            return new PayrollAnalyticsResult { ChartType = PayrollAnalyticsChartType.MonthlyTrend.ToString() };

        // Build the 12-month window ending at the anchor period.
        var periods = new List<(int Month, int Year)>();
        int m = anchorMonth, y = anchorYear;
        for (int i = 0; i < TrendMonths; i++)
        {
            periods.Add((m, y));
            m = m == 1 ? 12 : m - 1;
            if (m == 12) y -= 1;
        }
        periods.Reverse();

        var finalizedSlips = await FinalizedSlipsQuery()
            .Select(s => new { s.PayMonth, s.PayYear, s.GrossEarnings, s.TotalDeductions, s.NetSalary })
            .ToListAsync(ct);

        var categories = periods.Select(p => $"{p.Year:D4}-{p.Month:D2}").ToList();

        var grossPoints = new List<PayrollChartPoint>();
        var dedPoints = new List<PayrollChartPoint>();
        var netPoints = new List<PayrollChartPoint>();
        foreach (var p in periods)
        {
            var slice = finalizedSlips.Where(s => s.PayMonth == p.Month && s.PayYear == p.Year).ToList();
            string label = $"{p.Year:D4}-{p.Month:D2}";
            grossPoints.Add(new PayrollChartPoint { Label = label, Value = slice.Sum(s => s.GrossEarnings) });
            dedPoints.Add(new PayrollChartPoint { Label = label, Value = slice.Sum(s => s.TotalDeductions) });
            netPoints.Add(new PayrollChartPoint { Label = label, Value = slice.Sum(s => s.NetSalary) });
        }

        return new PayrollAnalyticsResult
        {
            ChartType = PayrollAnalyticsChartType.MonthlyTrend.ToString(),
            Categories = categories,
            Series =
            [
                new PayrollChartSeries { Name = "Gross", Points = grossPoints },
                new PayrollChartSeries { Name = "Deductions", Points = dedPoints },
                new PayrollChartSeries { Name = "Net", Points = netPoints },
            ],
        };
    }

    /// <summary>
    /// US-RPT-003 AC-2: department-wise salary distribution as a STACKED bar — per department, broken down by
    /// earnings components (Basic vs HRA/Allowances). Returns Categories = department names with two series
    /// ("Basic", "Allowances") so the FE renders a stacked bar; <see cref="PayrollAnalyticsResult.Points"/>
    /// also carries the per-department TOTAL salary cost (net) for a simple pie/table. Reuses
    /// DepartmentLookupsAsync + ComponentBreakdown. (Single-series net-per-department is preserved in Points
    /// for back-compat with the existing FE.)
    /// </summary>
    private async Task<PayrollAnalyticsResult> BuildDepartmentCostDistributionAsync(PayrollReportQueryParams qp, CancellationToken ct)
    {
        var (month, year, error) = await ResolvePeriodAsync(qp, ct);
        if (error is not null)
            return new PayrollAnalyticsResult { ChartType = PayrollAnalyticsChartType.DepartmentCostDistribution.ToString() };

        var slips = await ScopedSlipsForPeriodAsync(month, year, qp, ct);
        var (deptNameById, deptIdByEmployee) = await DepartmentLookupsAsync(slips, ct);
        var detailsBySlip = await DetailsBySlipAsync(slips, ct);

        string DeptOf(Guid empId)
        {
            var deptId = deptIdByEmployee.GetValueOrDefault(empId);
            return deptId is { } d ? deptNameById.GetValueOrDefault(d, "(Unassigned)") : "(Unassigned)";
        }

        // Per-department accumulation of basic / allowances / net.
        var byDept = new Dictionary<string, (decimal Basic, decimal Allowances, decimal Net)>();
        foreach (var slip in slips)
        {
            var dept = DeptOf(slip.EmployeeId);
            var comp = ComponentBreakdown.From(detailsBySlip.GetValueOrDefault(slip.Id, []));
            var acc = byDept.GetValueOrDefault(dept);
            byDept[dept] = (acc.Basic + comp.Basic, acc.Allowances + comp.Allowances, acc.Net + slip.NetSalary);
        }

        // Order departments by total net (descending) for a stable, meaningful axis.
        var orderedDepts = byDept.OrderByDescending(kv => kv.Value.Net).Select(kv => kv.Key).ToList();

        var points = orderedDepts
            .Select(d => new PayrollChartPoint { Label = d, Value = byDept[d].Net })
            .ToList();

        var series = new List<PayrollChartSeries>
        {
            new() { Name = "Basic", Points = orderedDepts.Select(d => new PayrollChartPoint { Label = d, Value = byDept[d].Basic }).ToList() },
            new() { Name = "Allowances", Points = orderedDepts.Select(d => new PayrollChartPoint { Label = d, Value = byDept[d].Allowances }).ToList() },
        };

        return new PayrollAnalyticsResult
        {
            ChartType = PayrollAnalyticsChartType.DepartmentCostDistribution.ToString(),
            Categories = orderedDepts,
            Series = series,
            Points = points,
        };
    }

    /// <summary>Statutory deduction breakdown — total per statutory component for the period (stacked bar).</summary>
    private async Task<PayrollAnalyticsResult> BuildStatutoryBreakdownAsync(PayrollReportQueryParams qp, CancellationToken ct)
    {
        var (month, year, error) = await ResolvePeriodAsync(qp, ct);
        if (error is not null)
            return new PayrollAnalyticsResult { ChartType = PayrollAnalyticsChartType.StatutoryBreakdown.ToString() };

        var slips = await ScopedSlipsForPeriodAsync(month, year, qp, ct);
        var detailsBySlip = await DetailsBySlipAsync(slips, ct);

        var points = detailsBySlip.Values
            .SelectMany(d => d)
            .Where(d => IsStatutory(d.ComponentType))
            .GroupBy(d => d.ComponentName)
            .Select(g => new PayrollChartPoint { Label = g.Key, Value = g.Sum(x => x.Amount) })
            .OrderByDescending(p => p.Value)
            .ToList();

        return new PayrollAnalyticsResult
        {
            ChartType = PayrollAnalyticsChartType.StatutoryBreakdown.ToString(),
            Points = points,
        };
    }

    // ══════════════════════════════════════════════════════════════
    //  Shared data access (tenant-scoped via the global query filter)
    // ══════════════════════════════════════════════════════════════

    /// <summary>Slips joined to FINALIZED runs only (BR-1). Tenant isolation via the global filter.</summary>
    private IQueryable<PayrollSlip> FinalizedSlipsQuery()
    {
        var finalizedRunIds = _dbContext.PayrollRuns.AsNoTracking()
            .Where(r => r.Status == PayrollRunStatus.Finalized)
            .Select(r => r.Id);

        return _dbContext.PayrollSlips.AsNoTracking()
            .Where(s => finalizedRunIds.Contains(s.PayrollRunId));
    }

    /// <summary>
    /// The period's finalized slips, narrowed by the FR-3 filters (department / job title / employment type /
    /// employee search). Department / job title / employment-type filters are applied via the employee set.
    /// </summary>
    private async Task<List<PayrollSlip>> ScopedSlipsForPeriodAsync(
        int month, int year, PayrollReportQueryParams qp, CancellationToken ct)
    {
        var query = FinalizedSlipsQuery();

        // ISSUE-178: DateFrom/DateTo scope the finalized-SLIP set by PAY-PERIOD year-month so a report can span
        // multiple months. When a range is supplied it REPLACES the single-period pin (the resolved
        // (month, year) still drives the period-specific columns — MoM / variance-vs-prev / YTD keep their own
        // single-period builders and are NOT rewired). When absent, the slip set is the single resolved period.
        if (qp.DateFrom is not null || qp.DateTo is not null)
        {
            if (qp.DateFrom is { } from)
            {
                int fromYm = from.Year * 12 + from.Month;
                query = query.Where(s => s.PayYear * 12 + s.PayMonth >= fromYm);
            }
            if (qp.DateTo is { } to)
            {
                int toYm = to.Year * 12 + to.Month;
                query = query.Where(s => s.PayYear * 12 + s.PayMonth <= toYm);
            }
        }
        else
        {
            query = query.Where(s => s.PayMonth == month && s.PayYear == year);
        }

        // FR-4: a specific finalized run (e.g. a supplementary run); else all finalized runs for the period.
        if (qp.PayrollRunId is { } runId)
            query = query.Where(s => s.PayrollRunId == runId);

        var slips = await query.ToListAsync(ct);

        if (slips.Count == 0)
            return slips;

        // Apply employee-side filters by restricting to the matching employee set.
        bool hasEmployeeFilter = qp.DepartmentId is not null || qp.JobTitleId is not null
            || qp.LocationId is not null || qp.SalaryStructureId is not null
            || !string.IsNullOrWhiteSpace(qp.EmploymentType) || !string.IsNullOrWhiteSpace(qp.EmployeeSearch);
        if (!hasEmployeeFilter)
            return slips;

        var allowedEmployeeIds = (await ScopedEmployeesQuery(qp)
                .Select(e => e.Id)
                .ToListAsync(ct))
            .ToHashSet();

        return slips.Where(s => allowedEmployeeIds.Contains(s.EmployeeId)).ToList();
    }

    /// <summary>The employee set matching the FR-3 filters (tenant-scoped via the global filter, BR-7: terminated NOT excluded).</summary>
    private IQueryable<Employee> ScopedEmployeesQuery(PayrollReportQueryParams qp)
    {
        IQueryable<Employee> query = _dbContext.Employees.AsNoTracking();

        if (qp.DepartmentId is { } deptId)
            query = query.Where(e => e.DepartmentId == deptId);
        if (qp.JobTitleId is { } jobId)
            query = query.Where(e => e.JobTitleId == jobId);
        if (qp.LocationId is { } locId)
            query = query.Where(e => e.LocationId == locId);
        if (qp.SalaryStructureId is { } structureId)
        {
            // ISSUE-178: no Employee.SalaryStructureId — narrow to employees with a CURRENTLY-effective
            // EmployeeSalaryComponent for this structure (mirrors the CTC report's effective-date filter).
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            query = query.Where(e => _dbContext.EmployeeSalaryComponents
                .Any(esc => esc.EmployeeId == e.Id
                            && esc.SalaryStructureId == structureId
                            && esc.EffectiveFrom <= today
                            && (esc.EffectiveTo == null || esc.EffectiveTo >= today)));
        }
        if (TryParseEmploymentType(qp.EmploymentType, out var empType))
            query = query.Where(e => e.EmploymentType == empType);
        if (!string.IsNullOrWhiteSpace(qp.EmployeeSearch))
        {
            var term = qp.EmployeeSearch.Trim().ToLower();
            query = query.Where(e =>
                e.FirstName.ToLower().Contains(term)
                || e.LastName.ToLower().Contains(term)
                || e.EmployeeNo.ToLower().Contains(term));
        }

        return query;
    }

    private async Task<Dictionary<Guid, List<PayrollSlipDetail>>> DetailsBySlipAsync(
        IReadOnlyList<PayrollSlip> slips, CancellationToken ct)
    {
        if (slips.Count == 0)
            return new Dictionary<Guid, List<PayrollSlipDetail>>();

        var slipIds = slips.Select(s => s.Id).ToList();
        var details = await _dbContext.PayrollSlipDetails.AsNoTracking()
            .Where(d => slipIds.Contains(d.PayrollSlipId))
            .ToListAsync(ct);

        return details.GroupBy(d => d.PayrollSlipId).ToDictionary(g => g.Key, g => g.ToList());
    }

    private async Task<Dictionary<Guid, Employee>> EmployeesByIdAsync(
        IReadOnlyList<PayrollSlip> slips, CancellationToken ct)
    {
        if (slips.Count == 0)
            return new Dictionary<Guid, Employee>();

        var empIds = slips.Select(s => s.EmployeeId).Distinct().ToList();
        return (await _dbContext.Employees.AsNoTracking()
                .Where(e => empIds.Contains(e.Id))
                .ToListAsync(ct))
            .ToDictionary(e => e.Id);
    }

    private async Task<(Dictionary<Guid, string> DeptNameById, Dictionary<Guid, Guid> DeptIdByEmployee)>
        DepartmentLookupsAsync(IReadOnlyList<PayrollSlip> slips, CancellationToken ct)
    {
        if (slips.Count == 0)
            return (new Dictionary<Guid, string>(), new Dictionary<Guid, Guid>());

        var empIds = slips.Select(s => s.EmployeeId).Distinct().ToList();
        var emps = await _dbContext.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.DepartmentId })
            .ToListAsync(ct);

        var deptIdByEmployee = emps.ToDictionary(e => e.Id, e => e.DepartmentId);
        var deptIds = emps.Select(e => e.DepartmentId).Distinct().ToList();

        var deptNameById = (await _dbContext.Departments.AsNoTracking()
                .Where(d => deptIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Name);

        return (deptNameById, deptIdByEmployee);
    }

    private async Task<Dictionary<Guid, string>> DepartmentNamesAsync(
        IReadOnlyList<Employee> employees, CancellationToken ct)
    {
        var deptIds = employees.Select(e => e.DepartmentId).Distinct().ToList();
        return (await _dbContext.Departments.AsNoTracking()
                .Where(d => deptIds.Contains(d.Id))
                .Select(d => new { d.Id, d.Name })
                .ToListAsync(ct))
            .ToDictionary(x => x.Id, x => x.Name);
    }

    /// <summary>
    /// Resolves the (month, year) to report on: the supplied filter, else the MOST RECENT finalized run's
    /// period (so a default report shows something useful). Returns an error message when there are no
    /// finalized runs to report on (404).
    /// </summary>
    private async Task<(int Month, int Year, string? Error)> ResolvePeriodAsync(
        PayrollReportQueryParams qp, CancellationToken ct)
    {
        if (qp.PayMonth is { } m && qp.PayYear is { } y)
            return (m, y, null);

        // FR-4: when a specific run is requested without an explicit period, resolve the period from the run.
        if (qp.PayrollRunId is { } runId)
        {
            var run = await _dbContext.PayrollRuns.AsNoTracking()
                .Where(r => r.Id == runId && r.Status == PayrollRunStatus.Finalized)
                .Select(r => new { r.PayMonth, r.PayYear })
                .FirstOrDefaultAsync(ct);
            if (run is null)
                return (0, 0, "The requested payroll run does not exist or is not finalized.");
            return (qp.PayMonth ?? run.PayMonth, qp.PayYear ?? run.PayYear, null);
        }

        var latest = await _dbContext.PayrollRuns.AsNoTracking()
            .Where(r => r.Status == PayrollRunStatus.Finalized)
            .OrderByDescending(r => r.PayYear).ThenByDescending(r => r.PayMonth)
            .Select(r => new { r.PayMonth, r.PayYear })
            .FirstOrDefaultAsync(ct);

        if (latest is null)
            return (0, 0, "No finalized payroll runs exist for this tenant.");

        return (qp.PayMonth ?? latest.PayMonth, qp.PayYear ?? latest.PayYear, null);
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    /// <summary>BR-2: mask all but the last 4 digits of an account number.</summary>
    public static string MaskAccount(string account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return string.Empty;
        var trimmed = account.Trim();
        if (trimmed.Length <= 4)
            return new string('*', trimmed.Length);
        return new string('*', trimmed.Length - 4) + trimmed[^4..];
    }

    /// <summary>Component-type split: Deduction + Statutory = deductions; Earning + Reimbursement = gross.</summary>
    private static bool IsStatutory(string componentType) =>
        string.Equals(componentType, nameof(SalaryComponentType.Statutory), StringComparison.OrdinalIgnoreCase);

    private static bool IsBasic(PayrollSlipDetail d) =>
        string.Equals(d.ComponentName, "Basic", StringComparison.OrdinalIgnoreCase)
        || string.Equals(d.ComponentName, "Basic Salary", StringComparison.OrdinalIgnoreCase)
        || (d.CalculationBasis?.Contains("BASIC", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool TryParseEmploymentType(string? value, out EmploymentType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var normalised = value.Replace("-", string.Empty).Replace(" ", string.Empty);
        return Enum.TryParse(normalised, ignoreCase: true, out type);
    }

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(string s) =>
        decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static readonly string[] MonthNames =
    {
        "", "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    };

    private static string MonthName(int month) => month is >= 1 and <= 12 ? MonthNames[month] : month.ToString(CultureInfo.InvariantCulture);

    // ── Per-slip component breakdown for the summary report ──────────────────

    /// <summary>US-RPT-003 AC-1: rolled-up period totals for the run summary.</summary>
    private readonly record struct PeriodTotals
    {
        public int EmployeeCount { get; init; }
        public decimal Gross { get; init; }
        public decimal Statutory { get; init; }
        public decimal Voluntary { get; init; }
        public decimal Net { get; init; }
        /// <summary>Statutory + voluntary deductions (the full deduction side).</summary>
        public decimal TotalDeductions => Statutory + Voluntary;
    }

    private struct DeptAccumulator
    {
        public int EmployeeCount;
        public decimal Basic;
        public decimal Allowances;
        public decimal Gross;
        public decimal Statutory;
        public decimal OtherDeductions;
        public decimal Net;
    }

    /// <summary>
    /// Splits a slip's detail lines into Basic / Allowances (other earnings + reimbursements) /
    /// Statutory / OtherDeductions (non-statutory deductions). Basic is identified by component name /
    /// calculation basis (no BASIC marker exists on the detail row, so the "Basic"/"Basic Salary" name is used).
    /// </summary>
    private readonly record struct ComponentBreakdown(
        decimal Basic, decimal Allowances, decimal Statutory, decimal OtherDeductions)
    {
        public static ComponentBreakdown From(IEnumerable<PayrollSlipDetail> details)
        {
            decimal basic = 0m, allowances = 0m, statutory = 0m, otherDeductions = 0m;
            foreach (var d in details)
            {
                bool isStatutory = string.Equals(d.ComponentType, nameof(SalaryComponentType.Statutory), StringComparison.OrdinalIgnoreCase);
                bool isDeduction = string.Equals(d.ComponentType, nameof(SalaryComponentType.Deduction), StringComparison.OrdinalIgnoreCase);

                if (isStatutory)
                    statutory += d.Amount;
                else if (isDeduction)
                    otherDeductions += d.Amount;
                else if (IsBasic(d))
                    basic += d.Amount;
                else
                    allowances += d.Amount; // other earnings + reimbursements
            }
            return new ComponentBreakdown(basic, allowances, statutory, otherDeductions);
        }
    }
}
