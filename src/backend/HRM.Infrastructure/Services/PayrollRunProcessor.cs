using System.Text;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
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
/// (AC-3). Re-running a ReviewPending/Cancelled run replaces its prior slips (FR-7). Finalized is immutable
/// (BR-7).</para>
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
    private readonly ILogger<PayrollRunProcessor> _logger;

    /// <summary>Synthetic component id for the LOP deduction line (BR-2). Stable so the slip detail FK is consistent.</summary>
    private static readonly Guid LopComponentId = Guid.Parse("00000000-0000-0000-0000-0000000010ce");

    /// <summary>Synthetic component id for adjustment lines (US-PAY-007). Stable so the slip detail FK is consistent.</summary>
    private static readonly Guid AdjustmentComponentId = Guid.Parse("00000000-0000-0000-0000-00000000ad57");

    public PayrollRunProcessor(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IAttendancePayrollService attendancePayroll,
        IPayrollNotificationService notifications,
        IStatutoryDeductionResolver statutoryResolver,
        IPayrollAdjustmentResolver adjustmentResolver,
        ILogger<PayrollRunProcessor> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _attendancePayroll = attendancePayroll;
        _notifications = notifications;
        _statutoryResolver = statutoryResolver;
        _adjustmentResolver = adjustmentResolver;
        _logger = logger;
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

        // NFR-4: log start with correlation (run id) + tenant context.
        _logger.LogInformation(
            "ProcessPayrollRun START. RunId={RunId}, Period={Year}-{Month}, Tenant={TenantId}",
            run.Id, run.PayYear, run.PayMonth, _tenantContext.TenantId);

        // FR-7: re-running a ReviewPending/Cancelled run replaces its prior slips. Remove the old slips +
        // details (soft-delete via SaveChanges is fine; we hard-remove to keep the run idempotent).
        await RemoveExistingSlipsAsync(run.Id, cancellationToken);

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

        // US-PAY-007 FR-3: Pending adjustments for the period, grouped by employee, loaded ONCE (no N+1). Empty
        // when none are configured → the per-employee lookup misses and the engine behaves exactly as before,
        // keeping every existing US-PAY-003/006 test green (purely additive wiring).
        var adjustmentsByEmployee = await _adjustmentResolver.ResolveForPeriodAsync(run.PayYear, run.PayMonth, cancellationToken);
        var appliedAdjustmentIds = new List<Guid>();

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
            decimal workingDays = attendance?.TotalWorkingDays > 0 ? attendance.TotalWorkingDays : workingDaysInMonth;
            decimal lopDays = attendance?.LopDays ?? 0m;

            // BR-4/BR-5: pro-rate mid-month joiners/leavers by the working days they were employed.
            decimal? proRataPaidDays = ProRataPaidDays(emp, monthStart, monthEnd, workingDays);

            var slipInput = new PayrollSlipInput(emp.Id, inputs, workingDays, lopDays, proRataPaidDays);
            var result = PayrollSlipCalculator.Compute(slipInput, LopComponentId);

            // US-PAY-007: this employee's Pending adjustments for the period (empty when none → no-op).
            var employeeAdjustments = adjustmentsByEmployee.GetValueOrDefault(emp.Id, EmployeeAdjustments.Empty);

            // US-PAY-007 ordering: TAXABLE bonuses are added to gross BEFORE statutory runs so the progressive
            // tax (US-PAY-006) is computed on the inflated gross. Non-taxable adjustments (reimbursements,
            // non-taxable bonuses, deductions, corrections) are added AFTER statutory so they never inflate the
            // tax base (BR-2/BR-4).
            result = ApplyTaxableAdjustments(result, employeeAdjustments);

            // US-PAY-006: when statutory rules are configured for the period, replace the structure's as-is
            // statutory lines with rule-computed deductions. No-op (returns `result`) when no rules exist.
            result = await ApplyStatutoryRulesAsync(result, run.PayYear, run.PayMonth, runLog, cancellationToken);

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

        await _dbContext.SaveChangesAsync(cancellationToken);

        // AC-3: notify HR (log-only seam until US-NTF). SignalR/email deferred — see module note.
        await _notifications.NotifyRunReadyForReviewAsync(_tenantContext.TenantId, run.Id, processed, skipped, cancellationToken);

        _logger.LogInformation(
            "ProcessPayrollRun END. RunId={RunId}, Processed={Processed}, Skipped={Skipped}, TotalNet={TotalNet}, Tenant={TenantId}",
            run.Id, processed, skipped, run.TotalNet, _tenantContext.TenantId);

        return Result.Success();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task RemoveExistingSlipsAsync(Guid runId, CancellationToken ct)
    {
        // US-PAY-007 FR-7 consistency: revert adjustments this run had marked Applied back to Pending so the
        // re-run re-picks them (otherwise a re-run would silently drop the adjustment lines). Double application
        // across DIFFERENT runs is still prevented — those stay Applied under their own run id.
        var appliedHere = await _dbContext.PayrollAdjustments
            .Where(a => a.AppliedInPayrollRunId == runId && a.Status == AdjustmentStatus.Applied)
            .ToListAsync(ct);
        foreach (var a in appliedHere)
        {
            a.Status = AdjustmentStatus.Pending;
            a.AppliedInPayrollRunId = null;
            a.UpdatedAt = DateTime.UtcNow;
        }

        var existingSlips = await _dbContext.PayrollSlips.Where(s => s.PayrollRunId == runId).ToListAsync(ct);
        if (existingSlips.Count == 0)
        {
            if (appliedHere.Count > 0) await _dbContext.SaveChangesAsync(ct);
            return;
        }

        var slipIds = existingSlips.Select(s => s.Id).ToList();
        var existingDetails = await _dbContext.PayrollSlipDetails
            .Where(d => slipIds.Contains(d.PayrollSlipId)).ToListAsync(ct);

        _dbContext.PayrollSlipDetails.RemoveRange(existingDetails);
        _dbContext.PayrollSlips.RemoveRange(existingSlips);
        await _dbContext.SaveChangesAsync(ct);
    }

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
        PayrollSlipResult result, int payYear, int payMonth, StringBuilder runLog, CancellationToken ct)
    {
        var basic = result.Lines
            .Where(l => l.Type == SalaryComponentType.Earning)
            .FirstOrDefault(l => string.Equals(l.Name, "Basic", StringComparison.OrdinalIgnoreCase)
                || string.Equals(l.Name, BasicCode, StringComparison.OrdinalIgnoreCase))
            .Amount;
        if (basic == 0m)
            basic = result.GrossEarnings; // no identifiable BASIC line → use gross as the contribution base.

        var wage = new StatutoryWageInput(
            MonthlyGross: result.GrossEarnings,
            MonthlyBasic: basic,
            ExemptEarnings: 0m,
            DeclaredExemptions: 0m,
            ComponentAmountsById: null);

        Result<StatutoryDeductions> resolved;
        try
        {
            resolved = await _statutoryResolver.ResolveAsync(payYear, payMonth, wage, fiscalYearOverride: null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Statutory resolution failed for employee {Employee}; applying structure statutory components as-is.", result.EmployeeId);
            return result;
        }

        // No rules in effect (the pre-US-PAY-006 path): keep the structure's as-is statutory lines.
        if (resolved.IsFailure || resolved.Value is null || string.IsNullOrEmpty(resolved.Value.FiscalYear) || resolved.Value.Lines.Count == 0)
            return result;

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

    private static string AdjustmentLabel(ResolvedAdjustment a) => a.AdjustmentType switch
    {
        AdjustmentType.Bonus => $"Bonus: {a.Description}",
        AdjustmentType.Reimbursement => $"Reimbursement: {a.Description}",
        AdjustmentType.Deduction => $"Deduction: {a.Description}",
        AdjustmentType.Correction => $"Arrears: {a.Description}",
        _ => a.Description,
    };

    /// <summary>Maps an employee's resolved salary-component rows to the engine's component inputs (FR-5a).</summary>
    private static List<PayrollComponentInput> BuildComponentInputs(
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
    /// BR-4/BR-5: when the employee joined after the month start, pro-rate by the working days from their
    /// joining date to the month end; full-month employees return null (no pro-ration). Separation pro-ration
    /// (BR-5) is driven by the attendance working-days for the terminated employee when present; the joiner
    /// case is computed here from the date of joining since it is always known on the employee record.
    /// </summary>
    private static decimal? ProRataPaidDays(Employee emp, DateOnly monthStart, DateOnly monthEnd, decimal workingDaysInMonth)
    {
        var doj = DateOnly.FromDateTime(emp.DateOfJoining);
        if (doj <= monthStart)
            return null; // joined before/at the month start — full month.
        if (doj > monthEnd)
            return 0m;   // joined after the period — no paid days.

        // Calendar-day proportion of the month worked from the joining date. Working-days granularity is the
        // attendance module's domain; for the joiner case we pro-rate the working-days baseline by the
        // fraction of the month actually employed (BR-4).
        var totalDays = monthEnd.DayNumber - monthStart.DayNumber + 1;
        var employedDays = monthEnd.DayNumber - doj.DayNumber + 1;
        var fraction = (decimal)employedDays / totalDays;
        return Math.Round(workingDaysInMonth * fraction, 2, MidpointRounding.AwayFromZero);
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
