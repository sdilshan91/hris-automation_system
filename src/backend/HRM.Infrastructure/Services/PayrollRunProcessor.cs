using System.Text;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using PA = HRM.Domain.Payroll.PayrollAuditAction;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// The compute side of the payroll engine (US-PAY-003 FR-3/FR-5). Invoked by the Hangfire
/// ProcessPayrollRunJob after it restores the tenant context (so the EF global query filter scopes every
/// query to the run's tenant — AC-7). Separated from the job so it can be exercised directly in tests
/// without a live Hangfire server.
///
/// <para>Per run it: sets <see cref="PayrollRunStatus.Processing"/>; fetches active employees with a current
/// salary structure; pulls the period attendance (working days, LOP, join/separation pro-ration) by reusing
/// the US-ATT-009 <see cref="IAttendancePayrollService"/>; computes each slip via the pure
/// <see cref="PayrollSlipCalculator"/> (LOP BR-2, pro-ration BR-4/BR-5, penny reconciliation BR-8); skips
/// employees without a structure with a run-log warning and continues (AC-6); batch-inserts the slips +
/// details (NFR-6); stamps the summary totals (FR-8); and moves the run to ReviewPending, notifying HR
/// (AC-3). Re-running a ReviewPending run replaces its prior slips (FR-7). Finalized and Cancelled runs are
/// terminal — the processor bails out rather than reprocessing them (BR-7 / ISSUE-154).</para>
///
/// <para>STATUTORY (FR-5c / US-PAY-006): when the tenant has configured statutory rules in effect for the
/// run's period, the <see cref="IStatutoryDeductionResolver"/> computes the employee-side statutory
/// deductions (progressive income tax over slabs, EPF with wage ceiling, ETF, professional/custom) and those
/// REPLACE the structure's as-is <c>is_statutory</c> component lines. When NO rules are configured (the
/// pre-US-PAY-006 case, and every existing US-PAY-003 test), the resolver returns an empty result and the
/// processor falls back to applying the structure's <c>is_statutory</c> components as-is — so this wiring is
/// purely additive and changes nothing until a tenant configures rules.</para>
/// </summary>
public sealed class PayrollRunProcessor : IPayrollRunProcessor
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IAttendancePayrollService _attendancePayroll;
    private readonly IPayrollNotificationService _notifications;
    private readonly IStatutoryDeductionResolver _statutoryResolver;
    private readonly IPayrollAdjustmentResolver _adjustmentResolver;
    private readonly IPayrollSlipCleaner _slipCleaner;
    private readonly IPayrollAuditLogger _audit;
    private readonly IHolidayProvider? _holidayProvider;
    private readonly ILogger<PayrollRunProcessor> _logger;

    /// <summary>Synthetic component id for the LOP deduction line (BR-2). Stable so the slip detail FK is consistent.</summary>
    private static readonly Guid LopComponentId = Guid.Parse("00000000-0000-0000-0000-0000000010ce");

    /// <summary>Synthetic component id for adjustment lines (US-PAY-007). Stable so the slip detail FK is consistent.</summary>
    private static readonly Guid AdjustmentComponentId = Guid.Parse("00000000-0000-0000-0000-00000000ad57");

    /// <summary>Synthetic component id for the overtime earning line (US-PAY-010 AC-2). Stable FK target.</summary>
    private static readonly Guid OvertimeComponentId = Guid.Parse("00000000-0000-0000-0000-00000000007e");

    /// <summary>Prefix the encashment adjustment description carries so the run can recognise + stamp it (US-PAY-010 AC-3).</summary>
    internal const string EncashmentDescriptionPrefix = "Leave Encashment";

    public PayrollRunProcessor(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IAttendancePayrollService attendancePayroll,
        IPayrollNotificationService notifications,
        IStatutoryDeductionResolver statutoryResolver,
        IPayrollAdjustmentResolver adjustmentResolver,
        IPayrollSlipCleaner slipCleaner,
        IPayrollAuditLogger audit,
        ILogger<PayrollRunProcessor> logger,
        IHolidayProvider? holidayProvider = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _attendancePayroll = attendancePayroll;
        _notifications = notifications;
        _statutoryResolver = statutoryResolver;
        _adjustmentResolver = adjustmentResolver;
        _slipCleaner = slipCleaner;
        _audit = audit;
        _logger = logger;
        // CAL-5: TRAILING-OPTIONAL so the many fixtures that compose their own DI container keep resolving
        // (mirrors OvertimeService's IHolidayProvider). DI always supplies the real HolidayProvider. With no
        // provider no holiday set can be built, so the exclusion policy cannot apply — which coincides with the
        // code-default (off). A fixture exercising the ON path MUST pass one.
        _holidayProvider = holidayProvider;
    }

    public async Task<Result> ProcessAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var run = await _dbContext.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, cancellationToken);
        if (run is null)
            return Result.Failure("Payroll run not found.", 404, "run_not_found");

        // BR-7: a Finalized run is immutable — never reprocess.
        if (run.Status == PayrollRunStatus.Finalized)
            return Result.Failure("A finalized payroll run cannot be reprocessed.", 409, "run_finalized");

        // ISSUE-154: a Cancelled run is terminal — a stale/duplicate enqueued job must NOT resurrect it back to
        // ReviewPending. Bail out (the ProcessPayrollRunJob logs the non-completion). Re-running a cancelled
        // period is a fresh initiate, not a reprocess of this run.
        if (run.Status == PayrollRunStatus.Cancelled)
            return Result.Failure("A cancelled payroll run cannot be reprocessed.", 409, "run_cancelled");

        // NFR-4: log start with correlation (run id) + tenant context.
        _logger.LogInformation(
            "ProcessPayrollRun START. RunId={RunId}, Period={Year}-{Month}, Tenant={TenantId}",
            run.Id, run.PayYear, run.PayMonth, _tenantContext.TenantId);

        // FR-7: re-running a ReviewPending run replaces its prior slips. Remove the old slips + details and
        // revert this run's Applied adjustments to Pending via the SHARED cleanup (the SAME helper the cancel
        // path uses — ISSUE-154). Hard-remove keeps the run idempotent.
        await _slipCleaner.RemoveRunSlipsAndRevertAdjustmentsAsync(run.Id, cancellationToken);

        run.Status = PayrollRunStatus.Processing;
        run.ProcessedEmployees = 0;
        run.SkippedEmployees = 0;
        run.TotalGross = run.TotalDeductions = run.TotalNet = run.TotalStatutory = 0m;
        run.RunLog = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var runLog = new StringBuilder();

        // Active employees (the global query filter already scopes to the tenant — AC-7).
        var employees = await _dbContext.Employees.AsNoTracking()
            .Where(e => e.IsActive
                && (e.Status == EmployeeStatus.Active || e.Status == EmployeeStatus.Probation))
            .OrderBy(e => e.EmployeeNo)
            .ToListAsync(cancellationToken);

        // ISSUE-294 (F&F Phase 1) double-pay boundary guard (money-critical). When a departing employee's final
        // period is owned by an F&F settlement (its policy had FinalPeriodOwnedBySettlement = true), the regular
        // run must NOT also pay that period — otherwise the final month is paid twice. Terminated employees are
        // already excluded by the Active/Probation filter above; this is the additive belt-and-suspenders for the
        // boundary month, where an employee could still read as Active while a settlement covering (LWD ≤ period
        // end) already exists. Exclude those employees. Empty when no settlements exist → pure no-op.
        var runMonthEnd = new DateOnly(run.PayYear, run.PayMonth, 1).AddMonths(1).AddDays(-1);
        var settlementOwnedEmployeeIds = await _dbContext.FinalSettlements.AsNoTracking()
            .Where(s => s.FinalPeriodOwnedBySettlement && s.LastWorkingDay <= runMonthEnd)
            .Select(s => s.EmployeeId)
            .ToListAsync(cancellationToken);
        if (settlementOwnedEmployeeIds.Count > 0)
        {
            var ownedSet = settlementOwnedEmployeeIds.ToHashSet();
            var beforeGuard = employees.Count;
            employees = employees.Where(e => !ownedSet.Contains(e.Id)).ToList();
            var excludedByGuard = beforeGuard - employees.Count;
            if (excludedByGuard > 0)
                runLog.AppendLine(
                    $"NOTE: {excludedByGuard} employee(s) excluded — final period owned by an F&F settlement (no double pay).");
        }

        run.TotalEmployees = employees.Count;

        // Current salary components for all employees in one pass (FR-5a).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var employeeIds = employees.Select(e => e.Id).ToList();

        var currentComponents = await _dbContext.EmployeeSalaryComponents.AsNoTracking()
            .Where(c => employeeIds.Contains(c.EmployeeId)
                && c.EffectiveFrom <= today
                && (c.EffectiveTo == null || c.EffectiveTo >= today))
            .ToListAsync(cancellationToken);

        var componentMeta = await LoadComponentMetaAsync(currentComponents, cancellationToken);
        var componentsByEmployee = currentComponents.GroupBy(c => c.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // ISSUE-165: resolve the department + job-title NAMES ONCE for the run (cheap, non-N+1 — mirrors
        // PayslipBatchRenderer.LoadRenderPlanAsync). These are stamped onto each slip as a point-in-time snapshot
        // so a later rename / department move never rewrites the historical slip. Tenant-scoped by the global filter.
        var departmentNames = await _dbContext.Departments.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => d.Name, cancellationToken);
        var jobTitleNames = await _dbContext.JobTitles.AsNoTracking()
            .ToDictionaryAsync(j => j.Id, j => j.TitleName, cancellationToken);

        // BR-3 / preconditions: LOP is applied ONLY when the attendance period is LOCKED/finalized (the
        // US-ATT-009 period lock). When the period is not locked, attendance is not final — absence of
        // clock-ins is not evidence of absence — so we treat everyone as fully present (LOP=0) and fall back to
        // the scheduled working-days baseline, rather than wiping salary for un-finalized periods. Hard
        // enforcement (block *initiate* when unlocked) is deferred — see the module note.
        //
        // Attendance data for the period (working days, LOP) — reuse US-ATT-009 (FR-5b). Pulled only when the
        // period is locked; a failed pull degrades to a full month with zero LOP and a log note.
        var periodLocked = await IsPeriodLockedAsync(run.PayYear, run.PayMonth, cancellationToken);
        var attendanceByEmployee = periodLocked
            ? await LoadAttendanceAsync(run.PayYear, run.PayMonth, employeeIds, runLog, cancellationToken)
            : new Dictionary<Guid, AttendancePayrollRowDto>();
        if (!periodLocked)
            runLog.AppendLine("NOTE: attendance period is not locked/finalized; LOP not applied (employees treated as fully present).");

        var (workingDaysInMonth, monthStart, monthEnd) = MonthBounds(run.PayYear, run.PayMonth);

        // ISSUE-156/157: shift-aware join/separation pro-ration. Resolve every employee's working-weekday set
        // (as-of monthStart — the SAME basis the attendance pull uses for TotalWorkingDays) and any in-period
        // separation date ONCE (no N+1). ProRataPaidDays then counts SHIFT working-days over the employed
        // sub-range, so a mid-month joiner/leaver is pro-rated on the run's working-days basis rather than a
        // calendar-day fraction — and, because the numerator uses the identical shift set as the denominator,
        // an employee WITH an attendance row is pro-rated exactly ONCE (see ProRataPaidDays).
        var proRationWorkingSets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            _dbContext, employeeIds, monthStart, cancellationToken);
        var separationDates = await SeparationDatesAsync(employeeIds, cancellationToken);

        // ── CAL-5 (US-ATT-011 AC-4/FR-5): holiday exclusion ─────────────────────────────────────────────────
        // When the tenant's EFFECTIVE calendar policy at monthStart says so, a public holiday is simply NOT a
        // working day. MONEY-CRITICAL: the SAME holiday set is threaded into BOTH sides of the pro-ration —
        // the DENOMINATOR (shiftWorkingDaysInMonth, below) and the NUMERATOR (ProRataPaidDays) — so the factor
        // stays single-basis. Excluding holidays from the denominator ALONE would raise a mid-month joiner's
        // factor and OVER-PAY them (silently clamped at 1.0 by PayrollSlipCalculator).
        //
        // Holidays are LOCATION-scoped (a Dubai holiday must not shrink a Colombo employee's working days), so
        // the sets are resolved ONCE per DISTINCT Employee.LocationId — this loop runs over every employee in
        // the tenant, so a per-employee provider call would be an N+1. Query cost: 1 policy + at most
        // (distinct locations + 1) holiday queries, flat in employee count.
        //
        // Flag OFF (the code-default, and every existing tenant): the map stays null, no holiday query runs, a
        // null set is threaded everywhere, and every figure is byte-identical to the pre-CAL-5 engine.
        // `_holidayProvider is not null` short-circuits BEFORE the policy query: with no provider no holiday set
        // can be built, so the exclusion cannot apply — coinciding with the code-default (off).
        var excludeHolidays = _holidayProvider is not null
            && await PayrollCalendarResolver.ExcludeHolidaysAsync(_dbContext, monthStart, cancellationToken);
        var holidaysByLocation = excludeHolidays
            ? await PayrollCalendarResolver.HolidaysByLocationAsync(
                _holidayProvider!, employees.Select(e => e.LocationId), monthStart, monthEnd, cancellationToken)
            : null;
        if (excludeHolidays)
            runLog.AppendLine("NOTE: public holidays are excluded from working days for this period (payroll calendar policy).");

        // US-PAY-007 FR-3: Pending adjustments for the period, grouped by employee, loaded ONCE (no N+1). Empty
        // when none are configured → the per-employee lookup misses and the engine behaves exactly as before,
        // keeping every existing US-PAY-003/006 test green (purely additive wiring).
        var adjustmentsByEmployee = await _adjustmentResolver.ResolveForPeriodAsync(run.PayYear, run.PayMonth, cancellationToken);
        var appliedAdjustmentIds = new List<Guid>();

        // ── Multi-country tax foundation ────────────────────────────────────────────────────────────────────
        // An employee is taxed under their BRANCH/Location's country; the tenant DEFAULT country is the fallback.
        // Resolve both ONCE for the run (no N+1 — mirrors the currentComponents/departmentNames batch loads):
        //   * locationCountry: LocationId → ISO CountryCode (only locations that have one).
        //   * tenantDefaultCountry: the fallback ISO code when an employee's location has no country.
        //   * statutoryConfigured: does the tenant have ANY active statutory rule in effect for THIS period?
        //     Only when true does a null tax country matter — so a tenant with no statutory rules (every
        //     pre-multi-country run) sees ZERO behaviour change (no warnings, no skips): purely additive.
        var locationCountry = await _dbContext.Locations.AsNoTracking()
            .Where(l => l.CountryCode != null)
            .Select(l => new { l.Id, l.CountryCode })
            .ToDictionaryAsync(x => x.Id, x => x.CountryCode!, cancellationToken);

        var tenantDefaultCountryRaw = await _dbContext.Tenants.AsNoTracking()
            .Where(t => t.Id == _tenantContext.TenantId)
            .Select(t => t.DefaultCountryCode)
            .FirstOrDefaultAsync(cancellationToken);
        var tenantDefaultCountry = string.IsNullOrWhiteSpace(tenantDefaultCountryRaw)
            ? null
            : tenantDefaultCountryRaw.Trim().ToUpperInvariant();

        var statutoryConfigured = await _dbContext.StatutoryRules.AsNoTracking()
            .AnyAsync(r => r.IsActive
                && r.EffectiveFrom <= monthEnd
                && (r.EffectiveTo == null || r.EffectiveTo >= monthStart), cancellationToken);

        // BACKWARD-COMPAT single-country fallback (money-critical). The distinct NON-NULL countries across the
        // tenant's applicable active rules for THIS period. When they span EXACTLY ONE country, employees with no
        // resolvable country fall back to it — so existing single-country tenants (no Location.CountryCode, no
        // Tenant.DefaultCountryCode) keep deducting statutory with ZERO setup, instead of silently under-taxing.
        // Only when rules span MULTIPLE countries is a country-less employee genuinely ambiguous → skip + flag.
        var ruleCountriesRaw = await _dbContext.StatutoryRules.AsNoTracking()
            .Where(r => r.IsActive
                && r.EffectiveFrom <= monthEnd
                && (r.EffectiveTo == null || r.EffectiveTo >= monthStart))
            .Select(r => r.CountryCode)
            .Distinct()
            .ToListAsync(cancellationToken);
        var ruleCountries = ruleCountriesRaw
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
        var soleRuleCountry = ruleCountries.Count == 1 ? ruleCountries[0] : null;

        // ── TAX-3: YTD-cumulative income tax (per-country) ──────────────────────────────────────────────────
        // Cumulative-vs-monthly is a per-country property on the IncomeTax rule. Resolve, ONCE per DISTINCT tax
        // country (no N+1), the effective IncomeTax rule's (EffectiveFrom, EffectiveTo, IsCumulative) for THIS
        // period — mirroring the resolver's country-filtered, per-type SelectEffective. Only a cumulative country
        // needs a prior-YTD lookup; a monthly (default) country threads 0/0 and behaves exactly as before.
        //
        // KNOWN LIMITATION (v1, acceptable): re-running an EARLIER month does NOT auto-recompute LATER months'
        // YTD — those later months would each need re-running to true-up. Finalized months are immutable, so this
        // only affects not-yet-finalized months.
        var periodDate = monthStart; // == FiscalYearResolver.PeriodDate(PayYear, PayMonth).
        var incomeTaxRuleRows = await _dbContext.StatutoryRules.AsNoTracking()
            .Where(r => r.IsActive
                && r.RuleType == StatutoryRuleType.IncomeTax
                && r.EffectiveFrom <= monthEnd
                && (r.EffectiveTo == null || r.EffectiveTo >= monthStart))
            .Select(r => new { r.CountryCode, r.EffectiveFrom, r.EffectiveTo, r.IsCumulative })
            .ToListAsync(cancellationToken);

        var incomeTaxByCountry = new Dictionary<string, (DateOnly From, DateOnly? To, bool IsCumulative)>();
        foreach (var grp in incomeTaxRuleRows
            .Where(r => !string.IsNullOrWhiteSpace(r.CountryCode))
            .GroupBy(r => r.CountryCode!.Trim().ToUpperInvariant()))
        {
            var candidates = grp.ToList();
            var ranges = candidates.Select(r => (r.EffectiveFrom, r.EffectiveTo)).ToList();
            var idx = FiscalYearResolver.SelectEffective(periodDate, ranges);
            if (idx >= 0)
                incomeTaxByCountry[grp.Key] = (candidates[idx].EffectiveFrom, candidates[idx].EffectiveTo, candidates[idx].IsCumulative);
        }
        var anyCumulative = incomeTaxByCountry.Values.Any(v => v.IsCumulative);

        // Prior slips for YTD accumulation — batch-loaded ONCE (no N+1), ONLY when a cumulative country is in play.
        // Window: periods strictly BEFORE the current period and within ~13 months back (covers an FY that spans the
        // calendar-year boundary, e.g. LK Apr–Mar). One active slip per employee+period is guaranteed by the cleaner
        // (cancelled/re-run slips are physically removed), so no status filter and no dedup are needed. Compared via
        // an ordinal (year*12+month) so the boundary works across December.
        var priorSlipsByEmployee = new Dictionary<Guid, List<(DateOnly Period, decimal Taxable, decimal Withheld)>>();
        if (anyCumulative)
        {
            var currentOrdinal = run.PayYear * 12 + run.PayMonth;
            var lookback = monthStart.AddMonths(-13);
            var lowerOrdinal = lookback.Year * 12 + lookback.Month;
            var priorRows = await _dbContext.PayrollSlips.AsNoTracking()
                .Where(s => employeeIds.Contains(s.EmployeeId)
                    && (s.PayYear * 12 + s.PayMonth) < currentOrdinal
                    && (s.PayYear * 12 + s.PayMonth) >= lowerOrdinal)
                .Select(s => new { s.EmployeeId, s.PayYear, s.PayMonth, s.TaxableIncome, s.IncomeTaxWithheld })
                .ToListAsync(cancellationToken);
            priorSlipsByEmployee = priorRows
                .GroupBy(s => s.EmployeeId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(s => (new DateOnly(s.PayYear, s.PayMonth, 1), s.TaxableIncome, s.IncomeTaxWithheld)).ToList());
        }

        var slips = new List<PayrollSlip>();
        var details = new List<PayrollSlipDetail>();
        int processed = 0, skipped = 0;
        decimal totalGross = 0m, totalDeductions = 0m, totalNet = 0m, totalStatutory = 0m;

        foreach (var emp in employees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!componentsByEmployee.TryGetValue(emp.Id, out var rows) || rows.Count == 0)
            {
                // AC-6: no salary structure assigned — skip with a warning, continue the run.
                skipped++;
                runLog.AppendLine($"SKIPPED {emp.EmployeeNo} ({emp.Id}): no active salary assignment.");
                continue;
            }

            var inputs = BuildComponentInputs(rows, componentMeta);

            var attendance = attendanceByEmployee.GetValueOrDefault(emp.Id);
            // Pro-ration DENOMINATOR. Prefer the attendance figure; otherwise fall back to this employee's
            // FULL-MONTH SHIFT working-days (the SAME resolved set / as-of monthStart the ProRataPaidDays
            // numerator uses) so the pro-ration factor is ALWAYS single-basis: shift/shift for a shift-
            // configured employee, calendar/calendar for a no-shift one (CountWorkingDays on an EMPTY set
            // counts every calendar day). The old calendar `workingDaysInMonth` fallback here mixed a SHIFT
            // numerator with a CALENDAR denominator (e.g. 9/30 instead of 9/22) and UNDER-PAID mid-month
            // joiners/leavers that had no attendance row.
            // CAL-5: this employee's LOCATION-scoped holiday set — null when the flag is off. The SAME instance
            // feeds the denominator and the numerator below; that is the single-basis guarantee.
            var empHolidays = PayrollCalendarResolver.For(holidaysByLocation, emp.LocationId);

            var shiftWorkingDaysInMonth = ShiftScheduleResolver.CountWorkingDays(
                proRationWorkingSets[emp.Id], monthStart, monthEnd, empHolidays);
            decimal workingDays = attendance?.TotalWorkingDays > 0
                ? attendance.TotalWorkingDays
                : (shiftWorkingDaysInMonth > 0 ? shiftWorkingDaysInMonth : workingDaysInMonth);
            decimal lopDays = attendance?.LopDays ?? 0m;

            // BR-4/BR-5: pro-rate mid-month joiners/leavers by the SHIFT working days they were employed.
            // CAL-5 (money-critical): `empHolidays` — the SAME set as the denominator above — is threaded here
            // too. Omitting it here while excluding holidays from the denominator inflates the factor and
            // over-pays joiners/leavers.
            DateOnly? separationDate = separationDates.TryGetValue(emp.Id, out var sep) ? sep : null;
            decimal? proRataPaidDays = ProRataPaidDays(
                emp, monthStart, monthEnd, proRationWorkingSets[emp.Id], separationDate, empHolidays);

            var slipInput = new PayrollSlipInput(emp.Id, inputs, workingDays, lopDays, proRataPaidDays);
            var result = PayrollSlipCalculator.Compute(slipInput, LopComponentId);

            // US-PAY-010 AC-2/FR-4: overtime earning. Derived from the attendance pull's APPROVED overtime
            // (BR-4 — pending/rejected are excluded upstream) per multiplier bucket (BR-5 holiday OT = its
            // bucket multiplier). hourly_rate = monthly_basic / (working_days * standard_hours_per_day). The
            // overtime line is added BEFORE statutory so it is in the tax base. Zero OT → no line, no-op.
            var overtime = ComputeOvertime(result, inputs, attendance, workingDays);
            if (!overtime.IsZero)
                result = ApplyOvertime(result, overtime);

            // US-PAY-007: this employee's Pending adjustments for the period (empty when none → no-op).
            var employeeAdjustments = adjustmentsByEmployee.GetValueOrDefault(emp.Id, EmployeeAdjustments.Empty);

            // US-PAY-010 AC-3/FR-5: total leave-encashment days/amount the period's adjustments carry for this
            // employee (an encashment is a Bonus adjustment whose description starts with the encashment prefix).
            var (encashmentDays, encashmentAmount) = EncashmentTotals(employeeAdjustments);

            // US-PAY-007 ordering: TAXABLE bonuses are added to gross BEFORE statutory runs so the progressive
            // tax (US-PAY-006) is computed on the inflated gross. Non-taxable adjustments (reimbursements,
            // non-taxable bonuses, deductions, corrections) are added AFTER statutory so they never inflate the
            // tax base (BR-2/BR-4).
            result = ApplyTaxableAdjustments(result, employeeAdjustments);

            // Multi-country tax foundation: the employee is taxed under their BRANCH/Location's country. Resolution
            // precedence: Location.CountryCode → Tenant.DefaultCountryCode → sole rule country (single-country
            // backward-compat fallback) → null. Null (only reachable for a MULTI-country tenant with no
            // location/tenant country) means the tax country is unresolved → statutory is SKIPPED + the employee
            // is flagged (never taxed under the wrong country).
            string? empCountry = null;
            if (emp.LocationId is { } locId && locationCountry.TryGetValue(locId, out var locCc))
                empCountry = locCc;
            empCountry ??= tenantDefaultCountry;
            empCountry = string.IsNullOrWhiteSpace(empCountry) ? null : empCountry.Trim().ToUpperInvariant();
            empCountry ??= soleRuleCountry;   // single-country backward-compat fallback (last resort).

            // TAX-3: when this employee's tax country runs a CUMULATIVE income-tax rule, sum their prior-period
            // TaxableIncome + IncomeTaxWithheld within the SAME fiscal year (period >= the rule's EffectiveFrom and
            // < the current period). Pure dict + in-memory sums over the pre-loaded prior slips (no per-employee
            // query). A monthly (default) country threads 0/0 — no lookup, no behaviour change.
            decimal priorTaxableYtd = 0m, priorTaxWithheldYtd = 0m;
            if (empCountry is not null
                && incomeTaxByCountry.TryGetValue(empCountry, out var itRule)
                && itRule.IsCumulative
                && priorSlipsByEmployee.TryGetValue(emp.Id, out var priorList))
            {
                foreach (var p in priorList)
                {
                    if (p.Period >= itRule.From && p.Period < monthStart)
                    {
                        priorTaxableYtd += p.Taxable;
                        priorTaxWithheldYtd += p.Withheld;
                    }
                }
            }

            // US-PAY-006: when statutory rules are configured for the period, replace the structure's as-is
            // statutory lines with rule-computed deductions. No-op (returns `result`) when no rules exist.
            result = await ApplyStatutoryRulesAsync(
                result, inputs, emp, empCountry, statutoryConfigured, run.PayYear, run.PayMonth,
                priorTaxableYtd, priorTaxWithheldYtd, runLog, cancellationToken);

            // US-PAY-007: remaining (non-tax-base) adjustment lines, then track applied ids for FR-4.
            result = ApplyNonTaxableAdjustments(result, employeeAdjustments);
            if (!employeeAdjustments.IsEmpty)
            {
                appliedAdjustmentIds.AddRange(employeeAdjustments.Adjustments.Select(a => a.AdjustmentId));
                runLog.AppendLine($"Adjustments ({employeeAdjustments.Adjustments.Count}) applied for employee {emp.Id}.");
            }

            var slip = new PayrollSlip
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                PayrollRunId = run.Id,
                EmployeeId = emp.Id,
                GrossEarnings = result.GrossEarnings,
                TotalDeductions = result.TotalDeductions,
                NetSalary = result.NetSalary,
                LopDays = result.LopDays,
                WorkingDays = result.WorkingDays,
                PaidDays = result.PaidDays,
                PayMonth = run.PayMonth,
                PayYear = run.PayYear,
                OvertimeHours = overtime.OvertimeHours,
                OvertimeAmount = overtime.OvertimeAmount,
                LeaveEncashmentDays = encashmentDays,
                LeaveEncashmentAmount = encashmentAmount,
                // TAX-3: income-tax basis persisted on every slip (0 when no income-tax rule / skipped / stripped).
                TaxableIncome = result.TaxableIncome,
                IncomeTaxWithheld = result.IncomeTaxWithheld,
                // ISSUE-165: stamp the resolved dept/designation NAMES at generation (null when the employee has
                // no/unknown dept or job title — the read path falls back to live resolution for null).
                DepartmentSnapshot = departmentNames.GetValueOrDefault(emp.DepartmentId),
                DesignationSnapshot = jobTitleNames.GetValueOrDefault(emp.JobTitleId),
                IsDeleted = false,
            };
            slips.Add(slip);

            foreach (var line in result.Lines)
            {
                details.Add(new PayrollSlipDetail
                {
                    Id = BaseEntity.NewUuidV7(),
                    TenantId = _tenantContext.TenantId,
                    PayrollSlipId = slip.Id,
                    SalaryComponentId = line.ComponentId,
                    ComponentName = line.Name,
                    ComponentCode = string.IsNullOrEmpty(line.Code) ? null : line.Code,
                    ComponentType = line.Type.ToString(),
                    Amount = line.Amount,
                    CalculationBasis = line.CalculationBasis,
                    IsDeleted = false,
                });
            }

            processed++;
            totalGross += result.GrossEarnings;
            totalDeductions += result.TotalDeductions;
            totalNet += result.NetSalary;
            totalStatutory += result.StatutoryTotal;
        }

        // NFR-6: batch insert all slips + details in one SaveChanges.
        if (slips.Count > 0) _dbContext.PayrollSlips.AddRange(slips);
        if (details.Count > 0) _dbContext.PayrollSlipDetails.AddRange(details);

        // US-PAY-007 FR-4: mark the included adjustments Applied with this run id, so they are not applied
        // again (a re-run reverts them first — see RemoveExistingSlipsAsync). Done at SLIP-PERSIST time (this
        // same SaveChanges that writes the slips), NOT at a separate Finalize step, so the applied state is
        // always consistent with the persisted slips. The resolver only flips tracked entities; this
        // SaveChanges commits them alongside the slips.
        await _adjustmentResolver.MarkAppliedAsync(appliedAdjustmentIds, run.Id, cancellationToken);

        // FR-8: stamp summary totals + counters; AC-3: move to ReviewPending on completion.
        run.ProcessedEmployees = processed;
        run.SkippedEmployees = skipped;
        run.TotalGross = Round(totalGross);
        run.TotalDeductions = Round(totalDeductions);
        run.TotalNet = Round(totalNet);
        run.TotalStatutory = Round(totalStatutory);
        run.Status = PayrollRunStatus.ReviewPending;
        run.CompletedAt = DateTime.UtcNow;
        run.RunLog = runLog.Length > 0 ? runLog.ToString().TrimEnd() : null;

        // US-PAY-012 (BUG-080): audit run completion. This runs in a Hangfire job with no HTTP user, so it is a
        // system actor (BR-7). Staged into the SAME SaveChanges that persists the completed run/slips so the
        // audit row is committed atomically with the completion.
        _audit.Log(PA.PayrollRunCompleted, PA.ResourceType.PayrollRun,
            run.Id.ToString(),
            before: null,
            after: new { run.PayYear, run.PayMonth, Status = run.Status.ToString(), Processed = processed, Skipped = skipped },
            systemActor: true);

        await _dbContext.SaveChangesAsync(cancellationToken);

        // AC-3: notify HR (log-only seam until US-NTF). SignalR/email deferred — see module note.
        await _notifications.NotifyRunReadyForReviewAsync(_tenantContext.TenantId, run.Id, processed, skipped, cancellationToken);

        _logger.LogInformation(
            "ProcessPayrollRun END. RunId={RunId}, Processed={Processed}, Skipped={Skipped}, TotalNet={TotalNet}, Tenant={TenantId}",
            run.Id, processed, skipped, run.TotalNet, _tenantContext.TenantId);

        return Result.Success();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, SalaryComponent>> LoadComponentMetaAsync(
        List<EmployeeSalaryComponent> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.SalaryComponentId).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, SalaryComponent>();
        return await _dbContext.SalaryComponents.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);
    }

    private const string BasicCode = "BASIC";

    /// <summary>
    /// US-PAY-006 integration: replaces the structure's as-is statutory lines with statutory-rule-computed
    /// deductions for the period, when rules are configured. Resolves via the shared
    /// <see cref="IStatutoryDeductionResolver"/> (so previewed test-calc numbers == run numbers), using the
    /// slip's gross (for tax / Gross-based contributions) and resolved BASIC line (for Basic-based EPF/ETF).
    ///
    /// <para>FAIL-OPEN / NO-OP: if no rules are in effect (empty fiscal year) or resolution fails, the
    /// original <paramref name="result"/> is returned unchanged, preserving the legacy as-is behaviour and
    /// keeping every existing US-PAY-003 test green.</para>
    /// </summary>
    private async Task<PayrollSlipResult> ApplyStatutoryRulesAsync(
        PayrollSlipResult result, IReadOnlyList<PayrollComponentInput> inputs, Employee emp,
        string? countryCode, bool statutoryConfigured, int payYear, int payMonth,
        decimal priorTaxableIncomeYtd, decimal priorTaxWithheldYtd, StringBuilder runLog, CancellationToken ct)
    {
        // Multi-country tax foundation (money-critical): when the employee's tax country cannot be resolved
        // (no branch/location country AND no tenant default), we must NEVER guess a country. Skip statutory for
        // this employee and FLAG them on the run — but only when statutory rules actually exist for the period
        // (statutoryConfigured), so a tenant with no statutory config sees no change. The rest of the slip
        // (earnings/other deductions) still computed; only the statutory pass is skipped.
        if (countryCode is null)
        {
            if (!statutoryConfigured)
                return result; // no statutory rules for the period → dormant feature, pure no-op (legacy behaviour).

            // Multi-country tenant with no location/tenant country for this employee → we cannot pick a country.
            return StripStatutoryAndFlag(result, emp, runLog, payYear, payMonth,
                "tax country could not be resolved (no branch/location country and no tenant default country)");
        }

        // OL-1 (BUG-078 sibling): identify BASIC by component Code, not display Name — otherwise Basic-based
        // EPF/ETF fell through to gross and over-deducted. Shared with the OT rate base via ResolvedBasic.
        var basic = ResolvedBasic(result, inputs);

        // TAX-2: map each earning/reimbursement line's ComponentId → its (pro-rated) amount so a
        // PercentOfComponent exemption can resolve the component it is a percentage of. Last write wins on a
        // duplicate component id (defensive; a structure has one line per component).
        var componentAmounts = new Dictionary<Guid, decimal>();
        foreach (var line in result.Lines)
        {
            if (line.ComponentId == Guid.Empty)
                continue;
            if (line.Type is SalaryComponentType.Earning or SalaryComponentType.Reimbursement)
                componentAmounts[line.ComponentId] = line.Amount;
        }

        var wage = new StatutoryWageInput(
            MonthlyGross: result.GrossEarnings,
            MonthlyBasic: basic,
            ExemptEarnings: 0m,
            DeclaredExemptions: 0m,
            ComponentAmountsById: componentAmounts,
            PriorTaxableIncomeYtd: priorTaxableIncomeYtd,   // TAX-3: 0 for a monthly (non-cumulative) rule.
            PriorTaxWithheldYtd: priorTaxWithheldYtd);

        Result<StatutoryDeductions> resolved;
        try
        {
            resolved = await _statutoryResolver.ResolveAsync(payYear, payMonth, wage, fiscalYearOverride: null, countryCode, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Statutory resolution failed for employee {Employee}; applying structure statutory components as-is.", result.EmployeeId);
            return result;
        }

        // FiscalYear (not line count) is the discriminator for "were rules resolved for this country?". A
        // resolved country whose income tax computes to 0 this period (below-threshold, zero income, or — TAX-3 —
        // a cumulative month still under the annual threshold) has a NON-EMPTY FiscalYear but ZERO lines: that is
        // an APPLIED result (0 deductions), NOT an unresolved country. We must still flow it through so the slip
        // persists TaxableIncome (needed for the TAX-3 YTD accumulation + the year-end statement). Only an EMPTY
        // FiscalYear means no rules were resolved for the country → strip + flag (or legacy no-op).
        if (resolved.IsFailure || resolved.Value is null || string.IsNullOrEmpty(resolved.Value.FiscalYear))
        {
            if (!statutoryConfigured)
                return result; // truly no rules in effect for the period → legacy no-op (pre-US-PAY-006 behaviour).

            // Money-critical: rules ARE configured, but NONE for THIS employee's resolved country. Never apply
            // another country's structure statutory lines to them — strip + flag (mirrors the null-country path).
            return StripStatutoryAndFlag(result, emp, runLog, payYear, payMonth,
                $"no statutory rules configured for tax country '{countryCode}'");
        }

        var deductions = resolved.Value;

        // Drop the structure's as-is statutory lines; keep earnings/reimbursements/non-statutory deductions (incl. LOP).
        var lines = result.Lines.Where(l => l.Type != SalaryComponentType.Statutory).ToList();

        // Add the rule-computed EMPLOYEE-side statutory lines (employer contributions are informational, not deducted).
        decimal statutoryTotal = 0m;
        foreach (var line in deductions.Lines.Where(l => !l.IsEmployerContribution))
        {
            lines.Add(new PayrollSlipLine(
                line.RuleId, line.Label, SalaryComponentType.Statutory, IsStatutory: true, line.Amount, line.Basis));
            statutoryTotal += line.Amount;
        }

        // Recompute the rolled-up totals from the adjusted line set (BR — statutory reduces net).
        decimal gross = 0m, totalDeductions = 0m;
        foreach (var l in lines)
        {
            switch (l.Type)
            {
                case SalaryComponentType.Earning:
                case SalaryComponentType.Reimbursement:
                    gross += l.Amount;
                    break;
                case SalaryComponentType.Deduction:
                case SalaryComponentType.Statutory:
                    totalDeductions += l.Amount;
                    break;
            }
        }

        gross = Round(gross);
        totalDeductions = Round(totalDeductions);
        var net = Round(gross - totalDeductions);

        runLog.AppendLine($"Statutory rules (FY {deductions.FiscalYear}) applied for employee {result.EmployeeId}: tax {deductions.IncomeTax:0.##}, EPF {deductions.EmployeeEpf:0.##}.");

        return result with
        {
            GrossEarnings = gross,
            TotalDeductions = totalDeductions,
            NetSalary = net,
            StatutoryTotal = Round(statutoryTotal),
            Lines = lines,
            // TAX-3: persist THIS month's taxable + the tax withheld this period (the YTD delta when cumulative).
            TaxableIncome = deductions.TaxableIncome,
            IncomeTaxWithheld = deductions.IncomeTax,
        };
    }

    /// <summary>
    /// Multi-country tax foundation (money-critical). The employee's tax country is either unresolvable
    /// (multi-country tenant, no location/tenant country) or resolved to a country the tenant has NO rules for.
    /// Either way we must NEVER apply a (possibly wrong) statutory line. DROP any structure-level as-is statutory
    /// lines, add NONE, and FLAG the employee on the run + Serilog with <paramref name="reason"/>. The rest of the
    /// slip (earnings / other deductions) is untouched. Returns <paramref name="result"/> unchanged when there was
    /// no statutory line to strip.
    /// </summary>
    private PayrollSlipResult StripStatutoryAndFlag(
        PayrollSlipResult result, Employee emp, StringBuilder runLog, int payYear, int payMonth, string reason)
    {
        runLog.AppendLine(
            $"WARNING {emp.EmployeeNo} ({emp.Id}): {reason}; statutory deductions SKIPPED.");
        _logger.LogWarning(
            "Payroll run {Year}-{Month}: employee {Employee} statutory skipped — {Reason}.",
            payYear, payMonth, emp.Id, reason);

        var kept = result.Lines.Where(l => l.Type != SalaryComponentType.Statutory).ToList();
        if (kept.Count == result.Lines.Count)
            return result; // nothing to strip.

        decimal g = 0m, d = 0m;
        foreach (var l in kept)
        {
            if (l.Type is SalaryComponentType.Earning or SalaryComponentType.Reimbursement) g += l.Amount;
            else if (l.Type == SalaryComponentType.Deduction) d += l.Amount;
        }
        g = Round(g);
        d = Round(d);
        return result with
        {
            GrossEarnings = g,
            TotalDeductions = d,
            NetSalary = Round(g - d),
            StatutoryTotal = 0m,
            Lines = kept,
        };
    }

    /// <summary>
    /// US-PAY-007 (pre-statutory pass): adds TAXABLE bonus adjustments as earning lines so they are in gross
    /// BEFORE the statutory pass computes progressive tax (BR-2 — a taxable bonus flows to tax). Returns the
    /// result unchanged when the employee has no taxable bonuses (so the existing tax flow is untouched).
    /// </summary>
    private static PayrollSlipResult ApplyTaxableAdjustments(PayrollSlipResult result, EmployeeAdjustments adjustments)
    {
        if (adjustments.IsEmpty) return result;

        var taxable = adjustments.Adjustments
            .Where(a => a.AdjustmentType == AdjustmentType.Bonus && a.IsTaxable)
            .ToList();
        if (taxable.Count == 0) return result;

        var lines = result.Lines.ToList();
        foreach (var a in taxable)
            lines.Add(new PayrollSlipLine(AdjustmentComponentId, AdjustmentLabel(a), SalaryComponentType.Earning, false, Round(a.Amount), "adjustment"));

        return RollUp(result, lines);
    }

    /// <summary>
    /// US-PAY-007 (post-statutory pass): adds the adjustment lines that must NOT inflate the tax base —
    /// reimbursements (earning, non-taxable by default BR-4), non-taxable bonuses (earning), deductions
    /// (subtract, BR-3), and corrections (arrears earning referencing the original slip, BR-5/FR-7). Returns
    /// the result unchanged when the employee has none of these.
    /// </summary>
    private static PayrollSlipResult ApplyNonTaxableAdjustments(PayrollSlipResult result, EmployeeAdjustments adjustments)
    {
        if (adjustments.IsEmpty) return result;

        var rest = adjustments.Adjustments
            .Where(a => !(a.AdjustmentType == AdjustmentType.Bonus && a.IsTaxable))
            .ToList();
        if (rest.Count == 0) return result;

        var lines = result.Lines.ToList();
        foreach (var a in rest)
        {
            switch (a.AdjustmentType)
            {
                case AdjustmentType.Bonus:        // non-taxable bonus → earning, outside the tax base.
                case AdjustmentType.Reimbursement: // BR-4: reimbursement is a (non-taxable) earning.
                    lines.Add(new PayrollSlipLine(AdjustmentComponentId, AdjustmentLabel(a), SalaryComponentType.Reimbursement, false, Round(a.Amount), "adjustment"));
                    break;
                case AdjustmentType.Deduction:    // BR-3: subtracted from net.
                    lines.Add(new PayrollSlipLine(AdjustmentComponentId, AdjustmentLabel(a), SalaryComponentType.Deduction, false, Round(a.Amount), "adjustment"));
                    break;
                case AdjustmentType.Correction:   // BR-5/FR-7: arrears line referencing the original slip.
                    var label = $"Arrears: {a.Description}" + (a.ReferencePayrollSlipId is { } slipId ? $" (ref {slipId})" : "");
                    lines.Add(new PayrollSlipLine(AdjustmentComponentId, label, SalaryComponentType.Reimbursement, false, Round(a.Amount), "arrears"));
                    break;
            }
        }

        return RollUp(result, lines);
    }

    /// <summary>Re-rolls gross/deductions/net from a line set after adjustment lines were added (US-PAY-007).</summary>
    private static PayrollSlipResult RollUp(PayrollSlipResult result, List<PayrollSlipLine> lines)
    {
        decimal gross = 0m, deductions = 0m, statutory = 0m;
        foreach (var l in lines)
        {
            switch (l.Type)
            {
                case SalaryComponentType.Earning:
                case SalaryComponentType.Reimbursement:
                    gross += l.Amount;
                    break;
                case SalaryComponentType.Deduction:
                    deductions += l.Amount;
                    break;
                case SalaryComponentType.Statutory:
                    deductions += l.Amount;
                    statutory += l.Amount;
                    break;
            }
        }

        gross = Round(gross);
        deductions = Round(deductions);
        return result with
        {
            GrossEarnings = gross,
            TotalDeductions = deductions,
            NetSalary = Round(gross - deductions),
            StatutoryTotal = Round(statutory),
            Lines = lines,
        };
    }

    /// <summary>
    /// US-PAY-010 AC-2/FR-4: computes the overtime earning for the employee from the attendance pull's approved
    /// overtime (per-multiplier buckets, BR-5) using the slip's resolved BASIC as the hourly-rate base. Returns
    /// <see cref="PayrollOvertimeCalculator.OvertimeResult.Zero"/> when there is no attendance row or no approved OT.
    /// </summary>
    private static PayrollOvertimeCalculator.OvertimeResult ComputeOvertime(
        PayrollSlipResult result, IReadOnlyList<PayrollComponentInput> inputs, AttendancePayrollRowDto? attendance, decimal workingDays)
    {
        if (attendance is null
            || (attendance.ApprovedOvertimeMinutes <= 0
                && (attendance.OvertimeMultiplierDetails is null || attendance.OvertimeMultiplierDetails.Count == 0)))
            return PayrollOvertimeCalculator.OvertimeResult.Zero;

        var basic = ResolvedBasic(result, inputs);
        return PayrollOvertimeCalculator.Compute(
            basic, workingDays, attendance.OvertimeMultiplierDetails, attendance.ApprovedOvertimeMinutes);
    }

    /// <summary>Adds the overtime earning line (US-PAY-010 AC-2) and re-rolls gross/net.</summary>
    private static PayrollSlipResult ApplyOvertime(PayrollSlipResult result, PayrollOvertimeCalculator.OvertimeResult overtime)
    {
        var lines = result.Lines.ToList();
        var basis = $"{overtime.OvertimeHours:0.##}h @ {overtime.HourlyRate:0.##}/h";
        lines.Add(new PayrollSlipLine(
            OvertimeComponentId, PayrollOvertimeCalculator.OvertimeLineName,
            SalaryComponentType.Earning, IsStatutory: false, overtime.OvertimeAmount, basis));
        return RollUp(result, lines);
    }

    /// <summary>
    /// The resolved (pro-rated) BASIC earning on a slip — the base for the OT hourly rate (US-PAY-010) and for
    /// Basic-based statutory contributions (US-PAY-006). Falls back to gross only when the structure has no
    /// identifiable BASIC line.
    /// <para>BUG-078 / OL-1: BASIC is identified by its component <b>Code</b> ("BASIC"), resolved to a
    /// ComponentId via <paramref name="inputs"/>, then read off the matching slip line — NOT by the display
    /// Name. Real structures name the line "Basic Salary", so the old Name-match fell through to
    /// <see cref="PayrollSlipResult.GrossEarnings"/>, over-basing the OT rate ~2.5× and over-deducting
    /// Basic-based EPF/ETF. This mirrors how every other consumer (LOP base, CTC, encashment) identifies BASIC.</para>
    /// </summary>
    internal static decimal ResolvedBasic(PayrollSlipResult result, IReadOnlyList<PayrollComponentInput> inputs)
    {
        var basicId = inputs
            .FirstOrDefault(i => string.Equals(i.Code, BasicCode, StringComparison.OrdinalIgnoreCase))
            .ComponentId;
        var basic = basicId == Guid.Empty
            ? 0m
            : result.Lines.FirstOrDefault(l => l.ComponentId == basicId).Amount;
        return basic > 0m ? basic : result.GrossEarnings;
    }

    /// <summary>
    /// US-PAY-010 AC-3/FR-5: totals the leave-encashment days + amount this employee's period adjustments carry.
    /// An encashment is a <see cref="AdjustmentType.Bonus"/> whose description starts with
    /// <see cref="EncashmentDescriptionPrefix"/>; the day count is parsed from the description suffix
    /// "(N days)". Returns (0, 0) when the employee has no encashment adjustment.
    /// </summary>
    private static (decimal Days, decimal Amount) EncashmentTotals(EmployeeAdjustments adjustments)
    {
        if (adjustments.IsEmpty) return (0m, 0m);

        decimal days = 0m, amount = 0m;
        foreach (var a in adjustments.Adjustments)
        {
            if (a.AdjustmentType != AdjustmentType.Bonus
                || !a.Description.StartsWith(EncashmentDescriptionPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            amount += a.Amount;
            days += ParseEncashmentDays(a.Description);
        }
        return (Round(days), Round(amount));
    }

    /// <summary>Parses the encashed-days count from an encashment description "...(N days)". 0 when absent.</summary>
    private static decimal ParseEncashmentDays(string description)
    {
        var open = description.LastIndexOf('(');
        if (open < 0) return 0m;
        var close = description.IndexOf(' ', open);
        if (close < 0) return 0m;
        var token = description.Substring(open + 1, close - open - 1);
        return decimal.TryParse(token, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static string AdjustmentLabel(ResolvedAdjustment a) => a.AdjustmentType switch
    {
        AdjustmentType.Bonus => $"Bonus: {a.Description}",
        AdjustmentType.Reimbursement => $"Reimbursement: {a.Description}",
        AdjustmentType.Deduction => $"Deduction: {a.Description}",
        AdjustmentType.Correction => $"Arrears: {a.Description}",
        _ => a.Description,
    };

    /// <summary>Maps an employee's resolved salary-component rows to the engine's component inputs (FR-5a).
    /// Internal so the negative-net advisory (BUG-074) can project a slip from the current structure without
    /// duplicating the row-to-input mapping.</summary>
    internal static List<PayrollComponentInput> BuildComponentInputs(
        List<EmployeeSalaryComponent> rows, Dictionary<Guid, SalaryComponent> meta)
    {
        var inputs = new List<PayrollComponentInput>(rows.Count);
        foreach (var r in rows)
        {
            meta.TryGetValue(r.SalaryComponentId, out var c);
            inputs.Add(new PayrollComponentInput(
                r.SalaryComponentId,
                c?.Code ?? string.Empty,
                c?.Name ?? "Component",
                c?.Type ?? SalaryComponentType.Earning,
                c?.IsStatutory ?? false,
                r.MonthlyAmount,
                c?.ProcessingOrder ?? 0));
        }
        return inputs;
    }

    /// <summary>
    /// BR-3: is the attendance period locked/finalized? Reuses the US-ATT-009 period-lock surface. A failed
    /// check degrades to "unlocked" so payroll still runs (without LOP) rather than hard-stopping.
    /// </summary>
    private async Task<bool> IsPeriodLockedAsync(int year, int month, CancellationToken ct)
    {
        try
        {
            var lockResult = await _attendancePayroll.GetPeriodLockAsync(year, month, ct);
            return lockResult.IsSuccess && lockResult.Value is { IsLocked: true };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Period-lock check failed for {Year}-{Month}; treating as unlocked.", year, month);
            return false;
        }
    }

    /// <summary>
    /// FR-5b: pulls the period attendance (working days + LOP days) per employee by reusing US-ATT-009. The
    /// attendance pull is best-effort: a failure (e.g. no attendance data) is logged and employees fall back
    /// to a full month with zero LOP, so payroll still runs (AC-2 degrades gracefully, not a hard stop).
    /// </summary>
    private async Task<Dictionary<Guid, AttendancePayrollRowDto>> LoadAttendanceAsync(
        int year, int month, IReadOnlyList<Guid> employeeIds, StringBuilder runLog, CancellationToken ct)
    {
        try
        {
            var pull = await _attendancePayroll.GetPayrollDataAsync(year, month, employeeIds, ct);
            if (pull.IsSuccess && pull.Value is not null)
                return pull.Value.Rows.ToDictionary(r => r.EmployeeId);

            runLog.AppendLine($"NOTE: attendance data unavailable ({pull.Error}); LOP/pro-ration default to full month.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Attendance pull failed for {Year}-{Month}; defaulting to full month.", year, month);
            runLog.AppendLine("NOTE: attendance pull failed; LOP/pro-ration default to full month.");
        }
        return new Dictionary<Guid, AttendancePayrollRowDto>();
    }

    /// <summary>
    /// BR-4/BR-5 (ISSUE-156 joiner / ISSUE-157 leaver): pro-rate a mid-month joiner/leaver by the SHIFT
    /// working-days they were actually employed within the period. The count is taken over the employed
    /// sub-range <c>[max(monthStart, DOJ) .. min(monthEnd, separationDate)]</c> using the employee's resolved
    /// working-weekday set via the SAME <see cref="ShiftScheduleResolver"/> the attendance pull uses to build
    /// <c>TotalWorkingDays</c>. Because the numerator and the working-days denominator are therefore on the
    /// identical shift basis, an employee WITH an attendance row is pro-rated exactly ONCE:
    /// <list type="bullet">
    ///   <item>Joiner: the attendance side does NOT start-bound joiners (TotalWorkingDays = full-month shift
    ///     days), so the single DOJ bound is applied here → factor = employed/full &lt; 1.</item>
    ///   <item>Leaver: the separation date used here is the SAME source as the attendance side's terminated
    ///     cutoff, so if attendance had already end-bounded the row the two counts are EQUAL → factor = 1 (no
    ///     second pro-ration). When attendance has no row for the leaver (the run's employees are Active/
    ///     Probation, which the attendance side does NOT cut off), this branch is the engine-side guard that
    ///     stops a separated employee being paid a full month.</item>
    /// </list>
    /// Returns null for a full-month employee (no pro-ration) and 0m when they were not employed on any day of
    /// the period.
    /// </summary>
    /// <remarks>
    /// ISSUE-294 (F&amp;F Phase 1): exposed <c>internal</c> so <c>RealPayrollFnFIntegration</c> reuses the EXACT
    /// leaver pro-ration (bounding paid days to the separation date) rather than duplicating the formula on a
    /// money path. The settlement passes the employee's last working day as <paramref name="separationDate"/>.
    /// </remarks>
    /// <param name="holidays">
    /// CAL-5 (US-ATT-011 AC-4/FR-5): the employee's LOCATION-scoped public-holiday set, or null to count
    /// holidays as working days (the pre-CAL-5 behaviour, and the default when the tenant's calendar policy
    /// leaves the flag off).
    /// <para><b>Money-critical — this is the NUMERATOR.</b> Whatever the caller passes here MUST match what it
    /// passed for the working-days DENOMINATOR. A holiday-excluded denominator with a holiday-inclusive
    /// numerator raises the pro-ration factor and OVER-PAYS mid-month joiners/leavers — and the overshoot is
    /// invisible, because <c>PayrollSlipCalculator</c> silently clamps <c>paidDaysBeforeLop</c> to
    /// <c>workingDays</c>. Trailing-optional so non-payroll callers keep the old semantics explicitly rather
    /// than by accident.</para>
    /// </param>
    internal static decimal? ProRataPaidDays(
        Employee emp, DateOnly monthStart, DateOnly monthEnd,
        HashSet<int> workingDaySet, DateOnly? separationDate,
        IReadOnlySet<DateOnly>? holidays = null)
    {
        var doj = DateOnly.FromDateTime(emp.DateOfJoining);
        if (doj > monthEnd)
            return 0m; // joined after the period — no paid days.

        var start = doj > monthStart ? doj : monthStart;   // ISSUE-156: joiner start bound.
        var end = monthEnd;
        if (separationDate is { } sepDate)
        {
            if (sepDate < monthStart)
                return 0m;             // separated before the period began — no paid days.
            if (sepDate < end)
                end = sepDate;         // ISSUE-157: leaver end bound.
        }

        // Full-month employee (joined on/before the month start with no in-period separation) — no pro-ration.
        if (start <= monthStart && end >= monthEnd)
            return null;

        if (end < start)
            return 0m; // separated before joining within the period.

        return ShiftScheduleResolver.CountWorkingDays(workingDaySet, start, end, holidays);
    }

    /// <summary>
    /// ISSUE-157: the in-period separation date per employee = most recent <c>status_change → Terminated</c>
    /// <see cref="EmploymentHistory.EffectiveDate"/>. Mirrors
    /// <c>AttendancePayrollService.TerminatedLastWorkingDaysAsync</c> so the leaver's paid-days bound matches
    /// the attendance side EXACTLY (guaranteeing no double pro-ration). Loaded ONCE for the whole run (no N+1);
    /// employees without such a record are absent from the result. Tenant-scoped via the global query filter.
    /// </summary>
    private async Task<Dictionary<Guid, DateOnly>> SeparationDatesAsync(
        IReadOnlyList<Guid> employeeIds, CancellationToken ct)
    {
        if (employeeIds.Count == 0) return new Dictionary<Guid, DateOnly>();

        var history = await _dbContext.EmploymentHistories.AsNoTracking()
            .Where(h => employeeIds.Contains(h.EmployeeId)
                && h.ChangeType == "status_change"
                && h.NewValue == "Terminated")
            .Select(h => new { h.EmployeeId, h.EffectiveDate })
            .ToListAsync(ct);

        return history
            .GroupBy(h => h.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => DateOnly.FromDateTime(g.Max(x => x.EffectiveDate)));
    }

    private static (decimal WorkingDays, DateOnly Start, DateOnly End) MonthBounds(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        // Default working-days baseline when attendance has no figure: calendar days in the month. The
        // attendance pull (when present) supplies the real scheduled working-days per employee.
        var days = end.DayNumber - start.DayNumber + 1;
        return (days, start, end);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
