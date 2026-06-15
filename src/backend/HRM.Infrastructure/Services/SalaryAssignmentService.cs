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
/// Employee salary-assignment service (US-PAY-002). All queries are tenant-scoped via ITenantContext +
/// the EF global query filter (AC-5). Resolves CTC breakdowns from a structure's effective component
/// rules via <see cref="CtcBreakdownCalculator"/> (FR-2/FR-3), persists employee_salary_component rows +
/// a salary_revision_history row (FR-1/FR-4/BR-3), honours future-dated vs immediate supersession
/// (BR-1/BR-2), and rejects inactive structures (FR-7) + CTC/sum mismatch beyond tolerance (FR-6).
/// </summary>
public sealed class SalaryAssignmentService : ISalaryAssignmentService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SalaryAssignmentService> _logger;

    /// <summary>FR-6 tolerance: |sum(earnings) - CTC| must be within ±1 currency unit.</summary>
    private const decimal CtcTolerance = 1m;

    public SalaryAssignmentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<SalaryAssignmentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<CtcBreakdownDto>> PreviewAsync(PreviewCtcInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CtcBreakdownDto>.Failure("Tenant context is not resolved.", 400);

        var loaded = await LoadStructureRulesAsync(input.SalaryStructureId, cancellationToken);
        if (loaded.IsFailure)
            return Result<CtcBreakdownDto>.Failure(loaded.Error!, loaded.StatusCode ?? 400, loaded.ErrorCode);

        var (structure, rules, components) = loaded.Value!;
        return BuildBreakdown(structure, rules, components, input.AnnualCtc, ToOverrideMap(input.Overrides));
    }

    public async Task<Result<CtcBreakdownDto>> AssignAsync(AssignSalaryStructureInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<CtcBreakdownDto>.Failure("Tenant context is not resolved.", 400);

        var employeeExists = await _dbContext.Employees.AsNoTracking().AnyAsync(e => e.Id == input.EmployeeId, cancellationToken);
        if (!employeeExists)
            return Result<CtcBreakdownDto>.Failure("Employee not found.", 404, "employee_not_found");

        var assignResult = await AssignInternalAsync(
            input.EmployeeId, input.SalaryStructureId, input.EffectiveFrom, input.AnnualCtc,
            ToOverrideMap(input.Overrides), input.Reason, cancellationToken);

        if (assignResult.IsFailure)
            return assignResult;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Salary structure assigned. EmployeeId={EmployeeId}, StructureId={StructureId}, CTC={Ctc}, EffectiveFrom={From}, TenantId={TenantId}, By={User}",
            input.EmployeeId, input.SalaryStructureId, input.AnnualCtc, input.EffectiveFrom, _tenantContext.TenantId, _currentUser.Email);

        return assignResult;
    }

    public async Task<Result<BulkAssignResultDto>> BulkAssignAsync(BulkAssignInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<BulkAssignResultDto>.Failure("Tenant context is not resolved.", 400);

        if (input.Employees.Count == 0)
            return Result<BulkAssignResultDto>.Failure("At least one employee is required for bulk assignment.", 400, "no_employees");

        // Validate the structure once up front (FR-7) — a bad structure fails the whole batch fast.
        var loaded = await LoadStructureRulesAsync(input.SalaryStructureId, cancellationToken);
        if (loaded.IsFailure)
            return Result<BulkAssignResultDto>.Failure(loaded.Error!, loaded.StatusCode ?? 400, loaded.ErrorCode);

        var validEmployeeIds = await _dbContext.Employees.AsNoTracking()
            .Where(e => input.Employees.Select(x => x.EmployeeId).Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        var validSet = validEmployeeIds.ToHashSet();

        var results = new List<BulkAssignResultItemDto>(input.Employees.Count);

        foreach (var entry in input.Employees)
        {
            if (!validSet.Contains(entry.EmployeeId))
            {
                results.Add(new BulkAssignResultItemDto
                {
                    EmployeeId = entry.EmployeeId, Success = false,
                    Error = "Employee not found.", ErrorCode = "employee_not_found",
                });
                continue;
            }

            var assign = await AssignInternalAsync(
                entry.EmployeeId, input.SalaryStructureId, input.EffectiveFrom, entry.AnnualCtc,
                ToOverrideMap(entry.Overrides), input.Reason, cancellationToken);

            results.Add(assign.IsSuccess
                ? new BulkAssignResultItemDto { EmployeeId = entry.EmployeeId, Success = true }
                : new BulkAssignResultItemDto
                {
                    EmployeeId = entry.EmployeeId, Success = false,
                    Error = assign.Error, ErrorCode = assign.ErrorCode,
                });
        }

        // Persist every staged change in one transaction (NFR-1 — single round-trip for up to 500 rows).
        await _dbContext.SaveChangesAsync(cancellationToken);

        var succeeded = results.Count(r => r.Success);

        _logger.LogInformation(
            "Bulk salary assignment completed. StructureId={StructureId}, Requested={Requested}, Succeeded={Succeeded}, TenantId={TenantId}, By={User}",
            input.SalaryStructureId, input.Employees.Count, succeeded, _tenantContext.TenantId, _currentUser.Email);

        return Result<BulkAssignResultDto>.Success(new BulkAssignResultDto
        {
            TotalRequested = input.Employees.Count,
            SucceededCount = succeeded,
            FailedCount = results.Count - succeeded,
            Results = results,
        });
    }

    public async Task<Result<EmployeeCompensationDto>> GetCurrentCompensationAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<EmployeeCompensationDto>.Failure("Tenant context is not resolved.", 400);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Current = rows whose validity window contains today (effective_from <= today, effective_to null or >= today).
        var rows = await _dbContext.EmployeeSalaryComponents.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId
                        && r.EffectiveFrom <= today
                        && (r.EffectiveTo == null || r.EffectiveTo >= today))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return Result<EmployeeCompensationDto>.Failure(
                "The employee has no active salary assignment.", 404, "no_active_assignment");

        // All current rows share one structure + effective_from (a single assignment writes them together).
        var structureId = rows[0].SalaryStructureId;
        var effectiveFrom = rows.Min(r => r.EffectiveFrom);

        var structure = await _dbContext.SalaryStructures.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == structureId, cancellationToken);

        var componentIds = rows.Select(r => r.SalaryComponentId).Distinct().ToList();
        var components = await _dbContext.SalaryComponents.AsNoTracking()
            .Where(c => componentIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        var lines = rows
            .Select(r => ToLineDto(r, components))
            .OrderBy(l => l.ProcessingOrder).ThenBy(l => l.ComponentCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var annualCtc = lines.Where(l => l.ComponentType == SalaryComponentType.Earning.ToString()).Sum(l => l.AnnualAmount);

        return Result<EmployeeCompensationDto>.Success(new EmployeeCompensationDto
        {
            EmployeeId = employeeId,
            SalaryStructureId = structureId,
            SalaryStructureName = structure?.Name ?? string.Empty,
            AnnualCtc = annualCtc,
            MonthlyCtc = Round(annualCtc / 12m),
            EffectiveFrom = effectiveFrom,
            Components = lines,
        });
    }

    public async Task<Result<IReadOnlyList<SalaryRevisionDto>>> GetRevisionHistoryAsync(Guid employeeId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<SalaryRevisionDto>>.Failure("Tenant context is not resolved.", 400);

        var revisions = await _dbContext.SalaryRevisionHistories.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.ChangedAt)
            .Select(r => new SalaryRevisionDto
            {
                Id = r.Id,
                EmployeeId = r.EmployeeId,
                OldStructureId = r.OldStructureId,
                NewStructureId = r.NewStructureId,
                OldAnnualCtc = r.OldAnnualCtc,
                NewAnnualCtc = r.NewAnnualCtc,
                EffectiveFrom = r.EffectiveFrom,
                Reason = r.Reason,
                ChangedBy = r.ChangedBy,
                ChangedAt = r.ChangedAt,
            })
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SalaryRevisionDto>>.Success(revisions);
    }

    // ── Core assignment (stages changes; caller owns SaveChanges) ─────────

    /// <summary>
    /// Validates + stages one assignment without saving: loads/validates the structure (FR-7), computes
    /// the breakdown (FR-2), enforces the CTC/sum tolerance (FR-6), supersedes prior CURRENT rows when the
    /// effective date is today-or-past (BR-1) while leaving them intact for a future date (BR-2), adds the
    /// new component rows + a revision-history row (BR-3), and returns the breakdown.
    /// </summary>
    private async Task<Result<CtcBreakdownDto>> AssignInternalAsync(
        Guid employeeId, Guid structureId, DateOnly effectiveFrom, decimal annualCtc,
        IReadOnlyDictionary<Guid, decimal> overrides, string? reason, CancellationToken cancellationToken)
    {
        var loaded = await LoadStructureRulesAsync(structureId, cancellationToken);
        if (loaded.IsFailure)
            return Result<CtcBreakdownDto>.Failure(loaded.Error!, loaded.StatusCode ?? 400, loaded.ErrorCode);

        var (structure, rules, components) = loaded.Value!;

        var breakdownResult = BuildBreakdown(structure, rules, components, annualCtc, overrides);
        if (breakdownResult.IsFailure)
            return breakdownResult;
        var breakdown = breakdownResult.Value!;

        // FR-6: sum of earning component values must equal the declared CTC within ±1.
        if (Math.Abs(breakdown.TotalAnnualEarnings - annualCtc) > CtcTolerance)
            return Result<CtcBreakdownDto>.Failure(
                $"The sum of earning components ({breakdown.TotalAnnualEarnings:0.##}) does not match the declared CTC ({annualCtc:0.##}) within the ±{CtcTolerance} tolerance.",
                400, "ctc_sum_mismatch");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Find the employee's currently-active rows (window contains today). Determine the prior structure
        // + CTC for the revision-history row (BR-3) regardless of supersession timing.
        var currentRows = await _dbContext.EmployeeSalaryComponents
            .Where(r => r.EmployeeId == employeeId
                        && r.EffectiveFrom <= today
                        && (r.EffectiveTo == null || r.EffectiveTo >= today))
            .ToListAsync(cancellationToken);

        Guid? oldStructureId = null;
        decimal? oldCtc = null;
        if (currentRows.Count > 0)
        {
            oldStructureId = currentRows[0].SalaryStructureId;
            oldCtc = currentRows
                .Where(r => string.Equals(ComponentType(r, components), SalaryComponentType.Earning.ToString(), StringComparison.Ordinal))
                .Sum(r => r.AnnualAmount);
        }

        // BR-1: a current-or-past effective date supersedes the active rows immediately (close them the day
        // before). BR-2: a FUTURE effective date does NOT touch the current rows — they stay active until
        // their own effective_to (here, until the new assignment's date arrives), so we set effective_to to
        // the day before the new assignment regardless; the difference is only whether "now" still falls in
        // the old window. We close the prior CURRENT rows at effectiveFrom-1 in both cases so the timeline
        // never overlaps; future-dated rows simply become current when the date arrives.
        foreach (var row in currentRows)
        {
            var closeOn = effectiveFrom.AddDays(-1);
            // Only shorten the window (never extend) and never close before it started.
            if ((row.EffectiveTo == null || row.EffectiveTo > closeOn) && closeOn >= row.EffectiveFrom)
                row.EffectiveTo = closeOn;
        }

        // Stage the new component rows.
        foreach (var line in breakdown.Components)
        {
            _dbContext.EmployeeSalaryComponents.Add(new EmployeeSalaryComponent
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                EmployeeId = employeeId,
                SalaryStructureId = structureId,
                SalaryComponentId = line.SalaryComponentId,
                AnnualAmount = line.AnnualAmount,
                MonthlyAmount = line.MonthlyAmount,
                IsOverride = line.IsOverride,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = null,
                IsDeleted = false,
            });
        }

        // BR-3: append a revision-history row capturing old/new structure + CTC, effective date, who/when.
        _dbContext.SalaryRevisionHistories.Add(new SalaryRevisionHistory
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            EmployeeId = employeeId,
            OldStructureId = oldStructureId,
            NewStructureId = structureId,
            OldAnnualCtc = oldCtc,
            NewAnnualCtc = annualCtc,
            EffectiveFrom = effectiveFrom,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            ChangedBy = _currentUser.UserId,
            ChangedAt = DateTime.UtcNow,
            IsDeleted = false,
        });

        return Result<CtcBreakdownDto>.Success(breakdown);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a structure (FR-7: must exist + be active) and its effective component rules (per-structure
    /// overrides merged into each component's method/value/formula) for CTC resolution.
    /// </summary>
    private async Task<Result<(SalaryStructure Structure, List<CtcComponentRule> Rules, Dictionary<Guid, SalaryComponent> Components)>>
        LoadStructureRulesAsync(Guid structureId, CancellationToken cancellationToken)
    {
        var structure = await _dbContext.SalaryStructures.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == structureId, cancellationToken);
        if (structure is null)
            return Result<(SalaryStructure, List<CtcComponentRule>, Dictionary<Guid, SalaryComponent>)>
                .Failure("Salary structure not found.", 404, "structure_not_found");

        // FR-7: an inactive/deactivated structure cannot be assigned.
        if (!structure.IsActive)
            return Result<(SalaryStructure, List<CtcComponentRule>, Dictionary<Guid, SalaryComponent>)>
                .Failure("An inactive salary structure cannot be assigned to an employee.", 400, "structure_inactive");

        var links = await _dbContext.SalaryStructureComponents.AsNoTracking()
            .Where(x => x.SalaryStructureId == structureId)
            .ToListAsync(cancellationToken);

        var componentIds = links.Select(l => l.SalaryComponentId).Distinct().ToList();
        var components = componentIds.Count == 0
            ? new Dictionary<Guid, SalaryComponent>()
            : await _dbContext.SalaryComponents.AsNoTracking()
                .Where(c => componentIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);

        var rules = new List<CtcComponentRule>(links.Count);
        foreach (var link in links)
        {
            if (!components.TryGetValue(link.SalaryComponentId, out var component))
                continue; // soft-deleted/missing component — skip defensively.

            var formula = !string.IsNullOrWhiteSpace(link.OverrideFormula)
                ? link.OverrideFormula
                : component.CalculationMethod == CalculationMethod.Formula ? component.FormulaExpression : null;

            var value = link.OverrideValue ?? component.DefaultValue;

            rules.Add(new CtcComponentRule(
                component.Id, component.Code, component.Type, component.CalculationMethod,
                value, formula, link.ProcessingOrder));
        }

        return Result<(SalaryStructure, List<CtcComponentRule>, Dictionary<Guid, SalaryComponent>)>
            .Success((structure, rules, components));
    }

    private static Result<CtcBreakdownDto> BuildBreakdown(
        SalaryStructure structure,
        List<CtcComponentRule> rules,
        Dictionary<Guid, SalaryComponent> components,
        decimal annualCtc,
        IReadOnlyDictionary<Guid, decimal> overrides)
    {
        if (annualCtc <= 0m)
            return Result<CtcBreakdownDto>.Failure("Annual CTC must be greater than zero.", 400, "invalid_ctc");

        IReadOnlyList<CtcComponentResult> resolved;
        try
        {
            resolved = CtcBreakdownCalculator.Calculate(rules, annualCtc, overrides);
        }
        catch (SalaryFormula.FormulaException ex)
        {
            return Result<CtcBreakdownDto>.Failure($"A component formula could not be evaluated: {ex.Message}", 400, "invalid_formula");
        }

        var lines = resolved.Select((r, i) =>
        {
            components.TryGetValue(r.ComponentId, out var component);
            return new CtcComponentLineDto
            {
                SalaryComponentId = r.ComponentId,
                ComponentName = component?.Name ?? string.Empty,
                ComponentCode = r.Code,
                ComponentType = r.Type.ToString(),
                AnnualAmount = r.AnnualAmount,
                MonthlyAmount = r.MonthlyAmount,
                IsOverride = r.IsOverride,
                ProcessingOrder = rules.FirstOrDefault(rule => rule.ComponentId == r.ComponentId).ProcessingOrder,
            };
        }).ToList();

        var totalAnnualEarnings = CtcBreakdownCalculator.TotalEarnings(resolved);

        return Result<CtcBreakdownDto>.Success(new CtcBreakdownDto
        {
            SalaryStructureId = structure.Id,
            AnnualCtc = annualCtc,
            TotalAnnualEarnings = totalAnnualEarnings,
            TotalMonthlyEarnings = Round(totalAnnualEarnings / 12m),
            Balanced = Math.Abs(totalAnnualEarnings - annualCtc) <= CtcTolerance,
            Components = lines,
        });
    }

    private static CtcComponentLineDto ToLineDto(EmployeeSalaryComponent row, Dictionary<Guid, SalaryComponent> components)
    {
        components.TryGetValue(row.SalaryComponentId, out var component);
        return new CtcComponentLineDto
        {
            SalaryComponentId = row.SalaryComponentId,
            ComponentName = component?.Name ?? string.Empty,
            ComponentCode = component?.Code ?? string.Empty,
            ComponentType = component?.Type.ToString() ?? string.Empty,
            AnnualAmount = row.AnnualAmount,
            MonthlyAmount = row.MonthlyAmount,
            IsOverride = row.IsOverride,
            ProcessingOrder = component?.ProcessingOrder ?? 0,
        };
    }

    private static string ComponentType(EmployeeSalaryComponent row, Dictionary<Guid, SalaryComponent> components)
        => components.TryGetValue(row.SalaryComponentId, out var c) ? c.Type.ToString() : string.Empty;

    private static IReadOnlyDictionary<Guid, decimal> ToOverrideMap(IReadOnlyList<SalaryOverrideInput> overrides)
        => overrides.Count == 0
            ? new Dictionary<Guid, decimal>()
            : overrides.GroupBy(o => o.SalaryComponentId).ToDictionary(g => g.Key, g => g.Last().AnnualAmount);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
