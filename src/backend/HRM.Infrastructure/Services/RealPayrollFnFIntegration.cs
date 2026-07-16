using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// The real <see cref="IPayrollFnFIntegration"/> (ISSUE-294 Phase 1) — replaces the LogOnly stub with a
/// tenant-configurable, effective-dated Full-and-Final settlement engine. On offboarding completion it computes
/// and persists a <see cref="FinalSettlement"/> for the departing employee, reusing the SAME building blocks the
/// monthly run uses so the numbers agree:
/// <list type="bullet">
///   <item>pro-rated final pay — <c>PayrollRunProcessor.ProRataPaidDays</c> (leaver branch, bounded to the LWD) +
///     <see cref="PayrollSlipCalculator"/> over the employee's current components;</item>
///   <item>statutory — <see cref="IStatutoryDeductionResolver"/> under the employee's resolved tax country;</item>
///   <item>leave encashment — the forfeitable-over-carry-forward math via
///     <see cref="LeaveCarryForwardCalculator"/> (the same authoritative calc the year-end job + the HR
///     encashment service use), consumed as an AMOUNT (terminated employees are excluded from runs, so it must
///     NOT be routed as a next-run adjustment).</item>
/// </list>
///
/// <para>Money-critical guards: idempotent on <c>OffboardingInstanceId</c> (a retried complete returns the
/// existing ref, never recomputes); a null/unresolvable tax country SKIPS statutory + records a flag (never
/// mis-taxes); the net payable is floored at 0 (never negative). Which components apply is driven by the
/// tenant's effective <see cref="TenantFnFPolicy"/> at the last working day; when none is configured a safe
/// code-default (all includes on) governs so the feature works without seeding.</para>
///
/// <para>Uses the passed <paramref name="lastWorkingDay"/> as the authoritative separation date — it does NOT
/// depend on an EmploymentHistory row (OffboardingService.CompleteAsync flips status without writing one).</para>
/// </summary>
public sealed class RealPayrollFnFIntegration : IPayrollFnFIntegration
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly IStatutoryDeductionResolver _statutoryResolver;
    private readonly ITenantLeaveYearResolver? _leaveYearResolver;
    private readonly ILogger<RealPayrollFnFIntegration> _logger;

    /// <summary>Synthetic LOP component id passed to <see cref="PayrollSlipCalculator.Compute"/> (LOP is always 0 here).</summary>
    private static readonly Guid LopComponentId = Guid.Parse("00000000-0000-0000-0000-0000000010ce");

    /// <summary>The code-default policy used when a tenant has configured no <see cref="TenantFnFPolicy"/>.</summary>
    private static readonly TenantFnFPolicy DefaultPolicy = new()
    {
        EffectiveFrom = DateOnly.MinValue,
        IncludeProRatedFinalPay = true,
        IncludeStatutory = true,
        IncludeLeaveEncashment = true,
        FinalPeriodOwnedBySettlement = true,
        IsActive = true,
    };

    public RealPayrollFnFIntegration(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        IStatutoryDeductionResolver statutoryResolver,
        ILogger<RealPayrollFnFIntegration> logger,
        ITenantLeaveYearResolver? leaveYearResolver = null)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _statutoryResolver = statutoryResolver;
        // ISSUE-305: trailing-optional so existing F&F fixtures keep compiling; DI always supplies it.
        // Absent => the last-working-day's calendar year, i.e. the pre-ISSUE-305 behaviour.
        _leaveYearResolver = leaveYearResolver;
        _logger = logger;
    }

    public async Task<Guid> TriggerFinalSettlementAsync(
        Guid tenantId,
        Guid employeeId,
        Guid offboardingInstanceId,
        DateTime lastWorkingDay,
        CancellationToken cancellationToken = default)
    {
        var lwd = DateOnly.FromDateTime(lastWorkingDay);

        // ── Idempotency (money-critical) ─────────────────────────────────────────────────────────────────────
        // If a settlement already exists for this offboarding instance, return its ref WITHOUT recomputing. A
        // retried offboarding-complete must never double-create. The unique index on offboarding_instance_id is
        // the DB-level backstop (Postgres 23505) for a concurrent race; this check covers the normal path.
        var existing = await _dbContext.FinalSettlements.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OffboardingInstanceId == offboardingInstanceId, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "F&F settlement already exists for offboarding {OffboardingId} (ref {SettlementRef}); returning existing.",
                offboardingInstanceId, existing.Id);
            return existing.Id;
        }

        var employee = await _dbContext.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            // Impossible on the offboarding path (the employee is validated before complete). Fail-closed with a
            // log + a non-persisted ref rather than throwing into the offboarding transaction.
            _logger.LogError(
                "F&F settlement skipped: employee {EmployeeId} not found (offboarding {OffboardingId}, tenant {TenantId}).",
                employeeId, offboardingInstanceId, tenantId);
            return BaseEntity.NewUuidV7();
        }

        // ── Resolve the effective policy at the LWD (latest EffectiveFrom ≤ LWD; else the code-default). ──────
        var policy = await _dbContext.TenantFnFPolicies.AsNoTracking()
            .Where(p => p.IsActive && p.EffectiveFrom <= lwd)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken)
            ?? DefaultPolicy;

        var monthStart = new DateOnly(lwd.Year, lwd.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        // Shift working-days for the LWD month (the SAME resolver the run + encashment use). Fall back to calendar
        // days when the employee has no resolvable shift (the resolver's empty-set rule counts every day).
        var workingDaySets = await ShiftScheduleResolver.ResolveWorkingDaySetsAsync(
            _dbContext, new[] { employeeId }, monthStart, cancellationToken);
        var shiftWorkingDays = ShiftScheduleResolver.CountWorkingDays(workingDaySets[employeeId], monthStart, monthEnd);
        decimal workingDays = shiftWorkingDays > 0 ? shiftWorkingDays : (monthEnd.DayNumber - monthStart.DayNumber + 1);

        // Current salary components as-of the LWD (the final-month structure).
        var componentRows = await _dbContext.EmployeeSalaryComponents.AsNoTracking()
            .Where(c => c.EmployeeId == employeeId
                && c.EffectiveFrom <= lwd
                && (c.EffectiveTo == null || c.EffectiveTo >= lwd))
            .ToListAsync(cancellationToken);
        var componentMeta = await LoadComponentMetaAsync(componentRows, cancellationToken);
        var inputs = PayrollRunProcessor.BuildComponentInputs(componentRows, componentMeta);

        // ── (3) Pro-rated final pay ──────────────────────────────────────────────────────────────────────────
        var lines = new List<FinalSettlementLine>();
        decimal proRatedGross = 0m;
        PayrollSlipResult? proRatedResult = null;
        if (policy.IncludeProRatedFinalPay && inputs.Count > 0)
        {
            // Reuse the EXACT leaver pro-ration: bound paid days to the LWD (separationDate = LWD).
            var proRataPaidDays = PayrollRunProcessor.ProRataPaidDays(
                employee, monthStart, monthEnd, workingDaySets[employeeId], lwd);
            var slipInput = new PayrollSlipInput(employeeId, inputs, workingDays, LopDays: 0m, proRataPaidDays);
            proRatedResult = PayrollSlipCalculator.Compute(slipInput, LopComponentId);

            // Only earnings/reimbursements form the settlement gross (statutory is computed separately below;
            // structure statutory lines are dropped to avoid double-counting). Structure non-statutory deductions
            // are NOT applied in Phase 1 — see the module note / OUT-OF-LANE flag.
            foreach (var line in proRatedResult.Lines)
            {
                if (line.Type is SalaryComponentType.Earning or SalaryComponentType.Reimbursement)
                {
                    lines.Add(NewLine(tenantId, line.Name, line.Amount, FinalSettlementLineType.Earning));
                    proRatedGross += line.Amount;
                }
            }
            proRatedGross = Round(proRatedGross);
        }

        // ── (2)/(4) Resolve tax country + statutory ──────────────────────────────────────────────────────────
        var (country, statutoryConfigured) = await ResolveCountryAsync(tenantId, employee, monthStart, monthEnd, cancellationToken);

        decimal statutoryTotal = 0m;
        var fiscalYear = string.Empty;
        var statutorySkipped = false;
        string? notes = null;
        if (policy.IncludeStatutory && proRatedResult is not null && proRatedGross > 0m)
        {
            if (country is null)
            {
                if (statutoryConfigured)
                {
                    // Money-critical: rules exist but the employee's tax country is unresolvable → never guess.
                    statutorySkipped = true;
                    notes = "Statutory skipped: tax country could not be resolved (no branch/location country and no tenant default).";
                    _logger.LogWarning(
                        "F&F {OffboardingId}: statutory SKIPPED for employee {EmployeeId} — {Reason}",
                        offboardingInstanceId, employeeId, notes);
                }
                // else: no statutory configured at all → nothing to apply, nothing to flag.
            }
            else
            {
                var basic = PayrollRunProcessor.ResolvedBasic(proRatedResult, inputs);
                var componentAmounts = new Dictionary<Guid, decimal>();
                foreach (var line in proRatedResult.Lines)
                {
                    if (line.ComponentId != Guid.Empty
                        && line.Type is SalaryComponentType.Earning or SalaryComponentType.Reimbursement)
                        componentAmounts[line.ComponentId] = line.Amount;
                }

                var wage = new StatutoryWageInput(
                    MonthlyGross: proRatedGross,
                    MonthlyBasic: basic,
                    ExemptEarnings: 0m,
                    DeclaredExemptions: 0m,
                    ComponentAmountsById: componentAmounts);
                    // PriorTaxable/PriorWithheld YTD default 0 — Phase 1 F&F does not thread cumulative YTD (see flag).

                var resolved = await _statutoryResolver.ResolveAsync(
                    lwd.Year, lwd.Month, wage, fiscalYearOverride: null, country, cancellationToken);

                if (resolved.IsSuccess && resolved.Value is { } deductions && !string.IsNullOrEmpty(deductions.FiscalYear))
                {
                    fiscalYear = deductions.FiscalYear;
                    foreach (var line in deductions.Lines.Where(l => !l.IsEmployerContribution))
                    {
                        lines.Add(NewLine(tenantId, line.Label, line.Amount, FinalSettlementLineType.Statutory));
                        statutoryTotal += line.Amount;
                    }
                    statutoryTotal = Round(statutoryTotal);
                }
                else if (statutoryConfigured)
                {
                    // Rules configured but NONE for this employee's resolved country → skip + flag (never apply
                    // another country's tax). Mirrors the run's StripStatutoryAndFlag.
                    statutorySkipped = true;
                    notes = $"Statutory skipped: no statutory rules configured for tax country '{country}'.";
                    _logger.LogWarning(
                        "F&F {OffboardingId}: statutory SKIPPED for employee {EmployeeId} — {Reason}",
                        offboardingInstanceId, employeeId, notes);
                }
            }
        }

        // ── (5) Forfeitable leave encashment ─────────────────────────────────────────────────────────────────
        decimal leaveEncashmentTotal = 0m;
        if (policy.IncludeLeaveEncashment)
        {
            var monthlyBasic = CurrentMonthlyBasic(inputs);
            if (monthlyBasic > 0m)
            {
                var dailyRate = Round(monthlyBasic / workingDays);
                // ISSUE-305: the LEAVE year the terminating employee's balance sits in — not `lwd.Year`.
                // ComputeLeaveEncashmentAsync reads the ledger by this key, so for an Apr-Mar tenant a
                // Jan-Mar last-working-day read a leave year that had not started yet => balance 0 => the
                // leave-encashment lines silently vanish from the final settlement (under-payment on exit).
                var leaveYear = _leaveYearResolver is null
                    ? lwd.Year
                    : await _leaveYearResolver.LabelForAsync(lwd, cancellationToken);
                var encashLines = await ComputeLeaveEncashmentAsync(employeeId, leaveYear, dailyRate, tenantId, cancellationToken);
                foreach (var (label, amount) in encashLines)
                {
                    lines.Add(NewLine(tenantId, label, amount, FinalSettlementLineType.Encashment));
                    leaveEncashmentTotal += amount;
                }
                leaveEncashmentTotal = Round(leaveEncashmentTotal);
            }
        }

        // ── (6) Net payable — floored at 0 (never negative). ─────────────────────────────────────────────────
        var netPayable = Round(proRatedGross + leaveEncashmentTotal - statutoryTotal);
        if (netPayable < 0m) netPayable = 0m;

        var settlement = new FinalSettlement
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            EmployeeId = employeeId,
            OffboardingInstanceId = offboardingInstanceId,
            LastWorkingDay = lwd,
            CountryCode = country,
            FiscalYear = fiscalYear,
            ProRatedGross = proRatedGross,
            StatutoryTotal = statutoryTotal,
            LeaveEncashmentTotal = leaveEncashmentTotal,
            NetPayable = netPayable,
            PolicyEffectiveFrom = policy.EffectiveFrom,
            FinalPeriodOwnedBySettlement = policy.FinalPeriodOwnedBySettlement,
            StatutorySkipped = statutorySkipped,
            Notes = notes,
            ComputedAtUtc = DateTime.UtcNow,
            Status = FinalSettlementStatus.Computed,
            Lines = lines,
            IsDeleted = false,
        };

        _dbContext.FinalSettlements.Add(settlement);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.GetBaseException() is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Idempotency under CONCURRENCY: a parallel offboarding-complete passed the pre-check at the same
            // time and won the `offboarding_instance_id` unique index (Postgres 23505). Recover to ITS ref rather
            // than throwing an unhandled violation out of OffboardingService.CompleteAsync (whose status flip is
            // already committed). The sequential path is covered by the pre-check above; this is the race backstop.
            _dbContext.Entry(settlement).State = EntityState.Detached;
            var winner = await _dbContext.FinalSettlements.AsNoTracking()
                .FirstAsync(s => s.OffboardingInstanceId == offboardingInstanceId, cancellationToken);
            _logger.LogWarning(
                "F&F settlement race on offboarding {OffboardingId}; returning the winning ref {SettlementRef}.",
                offboardingInstanceId, winner.Id);
            return winner.Id;
        }

        _logger.LogInformation(
            "F&F settlement computed. Ref={SettlementRef}, Employee={EmployeeId}, Offboarding={OffboardingId}, LWD={LWD}, " +
            "Gross={Gross}, Statutory={Statutory}, Encashment={Encashment}, Net={Net}, Tenant={TenantId}",
            settlement.Id, employeeId, offboardingInstanceId, lwd, proRatedGross, statutoryTotal,
            leaveEncashmentTotal, netPayable, tenantId);

        return settlement.Id;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Dictionary<Guid, SalaryComponent>> LoadComponentMetaAsync(
        List<EmployeeSalaryComponent> rows, CancellationToken ct)
    {
        var ids = rows.Select(r => r.SalaryComponentId).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, SalaryComponent>();
        return await _dbContext.SalaryComponents.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);
    }

    /// <summary>The employee's current monthly BASIC (un-prorated) — the encashment daily-rate base.</summary>
    private static decimal CurrentMonthlyBasic(IReadOnlyList<PayrollComponentInput> inputs)
    {
        foreach (var i in inputs)
            if (string.Equals(i.Code, "BASIC", StringComparison.OrdinalIgnoreCase))
                return i.MonthlyAmount;
        return 0m;
    }

    /// <summary>
    /// Resolves the employee's tax country using the SAME precedence the payroll run uses:
    /// Location.CountryCode → Tenant.DefaultCountryCode → sole-rule country (single-country backward-compat
    /// fallback) → null. Also reports whether ANY statutory rule is configured for the period (so a null country
    /// only matters when statutory actually exists).
    /// </summary>
    private async Task<(string? Country, bool StatutoryConfigured)> ResolveCountryAsync(
        Guid tenantId, Employee employee, DateOnly monthStart, DateOnly monthEnd, CancellationToken ct)
    {
        string? country = null;
        if (employee.LocationId is { } locId)
        {
            country = await _dbContext.Locations.AsNoTracking()
                .Where(l => l.Id == locId && l.CountryCode != null)
                .Select(l => l.CountryCode)
                .FirstOrDefaultAsync(ct);
        }

        if (string.IsNullOrWhiteSpace(country))
        {
            var tenantDefault = await _dbContext.Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId)
                .Select(t => t.DefaultCountryCode)
                .FirstOrDefaultAsync(ct);
            country = string.IsNullOrWhiteSpace(tenantDefault) ? null : tenantDefault;
        }

        country = string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant();

        // Statutory config presence + the sole-rule single-country fallback for the period.
        var ruleCountriesRaw = await _dbContext.StatutoryRules.AsNoTracking()
            .Where(r => r.IsActive
                && r.EffectiveFrom <= monthEnd
                && (r.EffectiveTo == null || r.EffectiveTo >= monthStart))
            .Select(r => r.CountryCode)
            .ToListAsync(ct);

        var statutoryConfigured = ruleCountriesRaw.Count > 0;
        if (country is null)
        {
            var ruleCountries = ruleCountriesRaw
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c!.Trim().ToUpperInvariant())
                .Distinct()
                .ToList();
            if (ruleCountries.Count == 1)
                country = ruleCountries[0]; // single-country backward-compat fallback (last resort).
        }

        return (country, statutoryConfigured);
    }

    /// <summary>
    /// Computes the forfeitable-leave encashment lines for the employee, mirroring
    /// <c>LeaveEncashmentService</c>'s BR-6 forfeitable-over-carry-forward ceiling (via the SAME
    /// <see cref="LeaveCarryForwardCalculator"/> the year-end job uses), then capping at the type's
    /// <c>MaxEncashDays</c>. The AMOUNT is consumed directly into the settlement (a terminated employee is
    /// excluded from runs, so it must NOT be routed as a next-run adjustment).
    /// </summary>
    private async Task<List<(string Label, decimal Amount)>> ComputeLeaveEncashmentAsync(
        Guid employeeId, int leaveYear, decimal dailyRate, Guid tenantId, CancellationToken ct)
    {
        var result = new List<(string, decimal)>();
        if (dailyRate <= 0m) return result;

        var encashableTypes = await _dbContext.LeaveTypes.AsNoTracking()
            .Where(t => t.Encashable && t.IsActive)
            .ToListAsync(ct);
        if (encashableTypes.Count == 0) return result;

        foreach (var type in encashableTypes)
        {
            var balance = await GetLedgerBalanceAsync(employeeId, type.Id, leaveYear, ct);

            decimal forfeitableCeiling = type.CarryForwardLimit is { } limit
                ? LeaveCarryForwardCalculator.Compute(balance, limit).ForfeitedDays
                : Math.Max(balance, 0m); // no carry-forward limit → the whole non-negative balance is encashable.

            var days = forfeitableCeiling;
            if (type.MaxEncashDays is { } max && days > max)
                days = max; // BR-6: cap the paid days at the type's maximum encashable days.

            if (days <= 0m) continue;

            var amount = Round(days * dailyRate);
            if (amount <= 0m) continue;

            result.Add(($"{type.Name} encashment ({days:0.##} day(s))", amount));
        }

        return result;
    }

    /// <summary>The employee's current available balance for a type/year = the most recent ledger BalanceAfter.</summary>
    private async Task<decimal> GetLedgerBalanceAsync(Guid employeeId, Guid leaveTypeId, int leaveYear, CancellationToken ct)
    {
        var lastEntry = await _dbContext.LeaveLedgerEntries.AsNoTracking()
            .Where(l => l.EmployeeId == employeeId && l.LeaveTypeId == leaveTypeId && l.LeaveYear == leaveYear)
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return lastEntry?.BalanceAfter ?? 0m;
    }

    private static FinalSettlementLine NewLine(Guid tenantId, string label, decimal amount, FinalSettlementLineType type) => new()
    {
        Id = BaseEntity.NewUuidV7(),
        TenantId = tenantId,
        Label = label.Length > 200 ? label[..200] : label,
        Amount = Round(amount),
        Type = type,
        IsDeleted = false,
    };

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
