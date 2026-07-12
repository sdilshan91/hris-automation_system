using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.DTOs;
using HRM.Application.Features.Payroll.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Domain.Payroll;
using PA = HRM.Domain.Payroll.PayrollAuditAction;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Salary-structure service (US-PAY-001). All queries are tenant-scoped via ITenantContext + the EF
/// global query filter (AC-6). Enforces: unique code per tenant (BR-2), single default per tenant
/// (BR-1), at-least-one-earning-before-active (FR-5), formula syntax + circular-ref validation across
/// the structure's components (BR-6), clone (FR-6), reorder (AC-4), and the statutory unlink guard (BR-3).
/// </summary>
public sealed class SalaryStructureService : ISalaryStructureService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly IPayrollAuditLogger _audit;
    private readonly ILogger<SalaryStructureService> _logger;

    private const int MaxPageSize = 100;

    public SalaryStructureService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        IPayrollAuditLogger audit,
        ILogger<SalaryStructureService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _audit = audit;
        _logger = logger;
    }

    /// <summary>
    /// US-PAY-012 (BUG-080): the audited snapshot of a salary structure (the fields whose change matters).
    /// Used as the before/after JSON payload; carries no salary amounts / PII.
    /// </summary>
    private static object Snapshot(SalaryStructure s) => new
    {
        s.Name, s.Code, s.Description, s.EffectiveFrom, s.IsDefault, s.IsActive,
    };

    public async Task<Result<SalaryStructureDto>> CreateAsync(SalaryStructureInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryStructureDto>.Failure("Tenant context is not resolved.", 400);

        var code = NormalizeCode(input.Code);

        if (await _dbContext.SalaryStructures.AnyAsync(s => s.Code == code, cancellationToken))
            return Result<SalaryStructureDto>.Failure(
                $"A salary structure with code '{code}' already exists.", 409, "duplicate_code");

        // Load the referenced components (tenant-scoped) so we can validate links + FR-5 + BR-6.
        var components = await LoadComponentsAsync(input.Components.Select(c => c.SalaryComponentId), cancellationToken);
        var linkCheck = ValidateLinks(input.Components, components, input.IsActive);
        if (linkCheck is not null)
            return Result<SalaryStructureDto>.Failure(linkCheck.Value.error, linkCheck.Value.status, linkCheck.Value.code);

        var structureId = BaseEntity.NewUuidV7();
        var structure = new SalaryStructure
        {
            Id = structureId,
            TenantId = _tenantContext.TenantId,
            Name = input.Name.Trim(),
            Code = code,
            Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim(),
            EffectiveFrom = input.EffectiveFrom,
            IsDefault = input.IsDefault,
            IsActive = input.IsActive,
            IsDeleted = false,
        };

        _dbContext.SalaryStructures.Add(structure);
        _dbContext.SalaryStructureComponents.AddRange(BuildLinks(structureId, input.Components));

        // BR-1: a new default clears any prior default for this tenant.
        if (input.IsDefault)
            await ClearExistingDefaultAsync(structureId, cancellationToken);

        // US-PAY-012 (BUG-080): audit the create — before=null (new), after=snapshot. Committed atomically.
        _audit.Log(PA.SalaryStructureCreated, PA.ResourceType.SalaryStructure,
            structure.Id.ToString(), before: null, after: Snapshot(structure));

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Salary structure created. Id={Id}, Code={Code}, TenantId={TenantId}, By={User}",
            structure.Id, structure.Code, _tenantContext.TenantId, _currentUser.Email);

        return await BuildDtoResultAsync(structureId, cancellationToken);
    }

    public async Task<Result<SalaryStructureDto>> UpdateAsync(Guid structureId, SalaryStructureInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryStructureDto>.Failure("Tenant context is not resolved.", 400);

        var structure = await _dbContext.SalaryStructures.FirstOrDefaultAsync(s => s.Id == structureId, cancellationToken);
        if (structure is null)
            return Result<SalaryStructureDto>.Failure("Salary structure not found.", 404);

        var code = NormalizeCode(input.Code);
        if (await _dbContext.SalaryStructures.AnyAsync(s => s.Code == code && s.Id != structureId, cancellationToken))
            return Result<SalaryStructureDto>.Failure(
                $"A salary structure with code '{code}' already exists.", 409, "duplicate_code");

        var components = await LoadComponentsAsync(input.Components.Select(c => c.SalaryComponentId), cancellationToken);
        var linkCheck = ValidateLinks(input.Components, components, input.IsActive);
        if (linkCheck is not null)
            return Result<SalaryStructureDto>.Failure(linkCheck.Value.error, linkCheck.Value.status, linkCheck.Value.code);

        // US-PAY-006 AC-5: a wholesale structure edit must not silently drop a mandatory statutory component.
        // The single-link UnlinkComponentAsync already guards this (BR-3); the update path replaces the whole
        // link set, so re-check here that every currently-mandatory statutory component survives the edit.
        var mandatoryGuard = await ValidateMandatoryStatutoryRetainedAsync(structureId, input.Components, cancellationToken);
        if (mandatoryGuard is not null)
            return Result<SalaryStructureDto>.Failure(mandatoryGuard.Value.error, 409, mandatoryGuard.Value.code);

        // US-PAY-012 (BUG-080): capture the BEFORE snapshot prior to mutating.
        var before = Snapshot(structure);

        structure.Name = input.Name.Trim();
        structure.Code = code;
        structure.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        structure.EffectiveFrom = input.EffectiveFrom;
        structure.IsActive = input.IsActive;
        structure.IsDefault = input.IsDefault;

        // Replace the link set wholesale (soft-delete the old, add the new) — simplest correct semantics
        // for a structure edit; historical payslips are never recomputed (AC-2) so old links can go.
        var existingLinks = await _dbContext.SalaryStructureComponents
            .Where(x => x.SalaryStructureId == structureId)
            .ToListAsync(cancellationToken);
        foreach (var link in existingLinks)
            link.IsDeleted = true;

        _dbContext.SalaryStructureComponents.AddRange(BuildLinks(structureId, input.Components));

        if (input.IsDefault)
            await ClearExistingDefaultAsync(structureId, cancellationToken);

        // US-PAY-012 (BUG-080): audit the update with before/after JSON of the changed fields.
        _audit.Log(PA.SalaryStructureUpdated, PA.ResourceType.SalaryStructure,
            structure.Id.ToString(), before: before, after: Snapshot(structure));

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Salary structure updated. Id={Id}, Code={Code}, TenantId={TenantId}, By={User}",
            structure.Id, structure.Code, _tenantContext.TenantId, _currentUser.Email);

        return await BuildDtoResultAsync(structureId, cancellationToken);
    }

    public async Task<Result<SalaryStructureDto>> GetByIdAsync(Guid structureId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryStructureDto>.Failure("Tenant context is not resolved.", 400);

        var exists = await _dbContext.SalaryStructures.AsNoTracking().AnyAsync(s => s.Id == structureId, cancellationToken);
        if (!exists)
            return Result<SalaryStructureDto>.Failure("Salary structure not found.", 404);

        return await BuildDtoResultAsync(structureId, cancellationToken);
    }

    public async Task<Result<PagedResult<SalaryStructureListItemDto>>> ListAsync(
        bool? isActive, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PagedResult<SalaryStructureListItemDto>>.Failure("Tenant context is not resolved.", 400);

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxPageSize ? Math.Clamp(pageSize, 1, MaxPageSize) : pageSize;

        var query = _dbContext.SalaryStructures.AsNoTracking();
        if (isActive.HasValue)
            query = query.Where(s => s.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(term) || s.Code.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var structures = await query
            .OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var ids = structures.Select(s => s.Id).ToList();
        var counts = await _dbContext.SalaryStructureComponents
            .Where(x => ids.Contains(x.SalaryStructureId))
            .GroupBy(x => x.SalaryStructureId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        var items = structures.Select(s => new SalaryStructureListItemDto
        {
            Id = s.Id,
            Name = s.Name,
            Code = s.Code,
            EffectiveFrom = s.EffectiveFrom,
            IsDefault = s.IsDefault,
            IsActive = s.IsActive,
            ComponentCount = counts.TryGetValue(s.Id, out var c) ? c : 0,
            CreatedAt = s.CreatedAt,
        }).ToList();

        return Result<PagedResult<SalaryStructureListItemDto>>.Success(new PagedResult<SalaryStructureListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result> DeleteAsync(Guid structureId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var structure = await _dbContext.SalaryStructures.FirstOrDefaultAsync(s => s.Id == structureId, cancellationToken);
        if (structure is null)
            return Result.Failure("Salary structure not found.", 404);

        structure.IsDeleted = true;
        var links = await _dbContext.SalaryStructureComponents
            .Where(x => x.SalaryStructureId == structureId)
            .ToListAsync(cancellationToken);
        foreach (var link in links)
            link.IsDeleted = true;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Salary structure soft-deleted. Id={Id}, TenantId={TenantId}, By={User}",
            structure.Id, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    public async Task<Result<SalaryStructureDto>> LinkComponentAsync(Guid structureId, SalaryStructureComponentInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryStructureDto>.Failure("Tenant context is not resolved.", 400);

        var structure = await _dbContext.SalaryStructures.FirstOrDefaultAsync(s => s.Id == structureId, cancellationToken);
        if (structure is null)
            return Result<SalaryStructureDto>.Failure("Salary structure not found.", 404);

        var component = await _dbContext.SalaryComponents.FirstOrDefaultAsync(c => c.Id == input.SalaryComponentId, cancellationToken);
        if (component is null)
            return Result<SalaryStructureDto>.Failure("The specified salary component does not exist.", 400, "component_not_found");

        if (await _dbContext.SalaryStructureComponents.AnyAsync(
                x => x.SalaryStructureId == structureId && x.SalaryComponentId == input.SalaryComponentId, cancellationToken))
            return Result<SalaryStructureDto>.Failure(
                "This component is already linked to the structure.", 409, "component_already_linked");

        if (!string.IsNullOrWhiteSpace(input.OverrideFormula))
        {
            var error = SalaryFormula.Validate(input.OverrideFormula);
            if (error is not null)
                return Result<SalaryStructureDto>.Failure($"Invalid override formula: {error}", 400, "invalid_formula");
        }

        _dbContext.SalaryStructureComponents.Add(new SalaryStructureComponent
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            SalaryStructureId = structureId,
            SalaryComponentId = input.SalaryComponentId,
            OverrideValue = input.OverrideValue,
            OverrideFormula = string.IsNullOrWhiteSpace(input.OverrideFormula) ? null : input.OverrideFormula.Trim(),
            ProcessingOrder = input.ProcessingOrder,
            IsMandatory = input.IsMandatory,
            IsDeleted = false,
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await BuildDtoResultAsync(structureId, cancellationToken);
    }

    public async Task<Result<SalaryStructureDto>> UnlinkComponentAsync(Guid structureId, Guid structureComponentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryStructureDto>.Failure("Tenant context is not resolved.", 400);

        var link = await _dbContext.SalaryStructureComponents
            .FirstOrDefaultAsync(x => x.Id == structureComponentId && x.SalaryStructureId == structureId, cancellationToken);
        if (link is null)
            return Result<SalaryStructureDto>.Failure("Component link not found.", 404);

        // BR-3: a mandatory statutory component cannot be removed from a structure.
        var component = await _dbContext.SalaryComponents.FirstOrDefaultAsync(c => c.Id == link.SalaryComponentId, cancellationToken);
        if (component is { IsStatutory: true } && link.IsMandatory)
            return Result<SalaryStructureDto>.Failure(
                "A mandatory statutory component cannot be removed from the structure.", 409, "statutory_mandatory");

        link.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await BuildDtoResultAsync(structureId, cancellationToken);
    }

    public async Task<Result<SalaryStructureDto>> ReorderComponentsAsync(
        Guid structureId, IReadOnlyList<(Guid StructureComponentId, int ProcessingOrder)> order, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryStructureDto>.Failure("Tenant context is not resolved.", 400);

        var exists = await _dbContext.SalaryStructures.AnyAsync(s => s.Id == structureId, cancellationToken);
        if (!exists)
            return Result<SalaryStructureDto>.Failure("Salary structure not found.", 404);

        var links = await _dbContext.SalaryStructureComponents
            .Where(x => x.SalaryStructureId == structureId)
            .ToListAsync(cancellationToken);
        var byId = links.ToDictionary(x => x.Id);

        foreach (var (id, processingOrder) in order)
        {
            if (!byId.TryGetValue(id, out var link))
                return Result<SalaryStructureDto>.Failure(
                    $"Component link {id} does not belong to this structure.", 400, "invalid_link");
            link.ProcessingOrder = processingOrder;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return await BuildDtoResultAsync(structureId, cancellationToken);
    }

    public async Task<Result<SalaryStructureDto>> CloneAsync(Guid structureId, string newName, string newCode, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryStructureDto>.Failure("Tenant context is not resolved.", 400);

        var source = await _dbContext.SalaryStructures.AsNoTracking().FirstOrDefaultAsync(s => s.Id == structureId, cancellationToken);
        if (source is null)
            return Result<SalaryStructureDto>.Failure("Salary structure not found.", 404);

        var code = NormalizeCode(newCode);
        if (await _dbContext.SalaryStructures.AnyAsync(s => s.Code == code, cancellationToken))
            return Result<SalaryStructureDto>.Failure(
                $"A salary structure with code '{code}' already exists.", 409, "duplicate_code");

        var sourceLinks = await _dbContext.SalaryStructureComponents.AsNoTracking()
            .Where(x => x.SalaryStructureId == structureId)
            .ToListAsync(cancellationToken);

        var cloneId = BaseEntity.NewUuidV7();
        var clone = new SalaryStructure
        {
            Id = cloneId,
            TenantId = _tenantContext.TenantId,
            Name = newName.Trim(),
            Code = code,
            Description = source.Description,
            EffectiveFrom = source.EffectiveFrom,
            IsDefault = false,             // a clone is never the default (BR-1).
            IsActive = source.IsActive,
            IsDeleted = false,
        };

        _dbContext.SalaryStructures.Add(clone);
        _dbContext.SalaryStructureComponents.AddRange(sourceLinks.Select(x => new SalaryStructureComponent
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            SalaryStructureId = cloneId,
            SalaryComponentId = x.SalaryComponentId,
            OverrideValue = x.OverrideValue,
            OverrideFormula = x.OverrideFormula,
            ProcessingOrder = x.ProcessingOrder,
            IsMandatory = x.IsMandatory,
            IsDeleted = false,
        }));

        // US-PAY-012 (BUG-080): a clone materializes a NEW structure — audit it as a Created.
        _audit.Log(PA.SalaryStructureCreated, PA.ResourceType.SalaryStructure,
            clone.Id.ToString(), before: null, after: Snapshot(clone));

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Salary structure cloned. Source={Source}, Clone={Clone}, TenantId={TenantId}, By={User}",
            structureId, cloneId, _tenantContext.TenantId, _currentUser.Email);

        return await BuildDtoResultAsync(cloneId, cancellationToken);
    }

    // ── Validation helpers ───────────────────────────────────────────

    /// <summary>
    /// Validates a structure's component links: every referenced component must exist + be in-tenant; no
    /// duplicates; override formulas parse; FR-5 (at least one earning when active); and BR-6 circular
    /// references across formula components (using each component's effective formula).
    /// </summary>
    private static (string error, int status, string code)? ValidateLinks(
        IReadOnlyList<SalaryStructureComponentInput> inputs,
        Dictionary<Guid, SalaryComponent> components,
        bool isActive)
    {
        if (inputs.Count == 0)
        {
            // An empty structure is allowed only while inactive (FR-5 requires an earning to activate).
            return isActive
                ? ("A salary structure must have at least one earning component before it can be active.", 400, "no_earning_component")
                : null;
        }

        if (inputs.Select(i => i.SalaryComponentId).Distinct().Count() != inputs.Count)
            return ("The same component cannot be linked to a structure more than once.", 400, "duplicate_component");

        foreach (var input in inputs)
        {
            if (!components.ContainsKey(input.SalaryComponentId))
                return ($"Component {input.SalaryComponentId} does not exist.", 400, "component_not_found");

            if (!string.IsNullOrWhiteSpace(input.OverrideFormula))
            {
                var error = SalaryFormula.Validate(input.OverrideFormula);
                if (error is not null)
                    return ($"Invalid override formula: {error}", 400, "invalid_formula");
            }
        }

        // FR-5: at least one Earning component to be active.
        if (isActive && !inputs.Any(i => components[i.SalaryComponentId].Type == SalaryComponentType.Earning))
            return ("A salary structure must have at least one earning component before it can be active.", 400, "no_earning_component");

        // BR-6: circular references among formula components within the structure.
        var cycleError = DetectCircularReferences(inputs, components);
        if (cycleError is not null)
            return (cycleError, 400, "circular_reference");

        return null;
    }

    /// <summary>
    /// Builds a dependency graph keyed by component code (the formula variable namespace) and detects a
    /// cycle. A component's effective formula is its per-structure override, else the component default.
    /// </summary>
    private static string? DetectCircularReferences(
        IReadOnlyList<SalaryStructureComponentInput> inputs,
        Dictionary<Guid, SalaryComponent> components)
    {
        var dependencies = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in inputs)
        {
            var component = components[input.SalaryComponentId];
            var formula = !string.IsNullOrWhiteSpace(input.OverrideFormula)
                ? input.OverrideFormula
                : component.CalculationMethod == CalculationMethod.Formula ? component.FormulaExpression : null;

            if (string.IsNullOrWhiteSpace(formula))
                continue;

            // Already syntax-validated above; ReferencedVariables is safe here.
            dependencies[component.Code] = SalaryFormula.ReferencedVariables(formula);
        }

        if (dependencies.Count == 0)
            return null;

        var cycle = FormulaDependencyGraph.FindCycle(dependencies);
        return cycle is null
            ? null
            : $"Circular reference detected among formula components: {string.Join(" → ", cycle)}.";
    }

    private async Task<Dictionary<Guid, SalaryComponent>> LoadComponentsAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
            return new();
        return await _dbContext.SalaryComponents
            .Where(c => idList.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
    }

    private IEnumerable<SalaryStructureComponent> BuildLinks(Guid structureId, IReadOnlyList<SalaryStructureComponentInput> inputs)
        => inputs.Select(i => new SalaryStructureComponent
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            SalaryStructureId = structureId,
            SalaryComponentId = i.SalaryComponentId,
            OverrideValue = i.OverrideValue,
            OverrideFormula = string.IsNullOrWhiteSpace(i.OverrideFormula) ? null : i.OverrideFormula.Trim(),
            ProcessingOrder = i.ProcessingOrder,
            IsMandatory = i.IsMandatory,
            IsDeleted = false,
        });

    /// <summary>
    /// US-PAY-006 AC-5: ensures a wholesale structure edit retains every component that is currently a
    /// MANDATORY STATUTORY link. Returns an error when such a component would be dropped from the structure;
    /// null when all mandatory statutory components are still present in the new input set.
    /// </summary>
    private async Task<(string error, string code)?> ValidateMandatoryStatutoryRetainedAsync(
        Guid structureId, IReadOnlyList<SalaryStructureComponentInput> newInputs, CancellationToken cancellationToken)
    {
        var mandatoryLinks = await _dbContext.SalaryStructureComponents.AsNoTracking()
            .Where(x => x.SalaryStructureId == structureId && x.IsMandatory)
            .ToListAsync(cancellationToken);
        if (mandatoryLinks.Count == 0)
            return null;

        var mandatoryComponentIds = mandatoryLinks.Select(l => l.SalaryComponentId).Distinct().ToList();
        var statutoryMandatoryIds = await _dbContext.SalaryComponents.AsNoTracking()
            .Where(c => mandatoryComponentIds.Contains(c.Id) && c.IsStatutory)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
        if (statutoryMandatoryIds.Count == 0)
            return null;

        var retainedIds = newInputs.Select(i => i.SalaryComponentId).ToHashSet();
        if (statutoryMandatoryIds.Any(id => !retainedIds.Contains(id)))
            return ("A mandatory statutory component cannot be removed from the structure.", "statutory_mandatory");

        return null;
    }

    private async Task ClearExistingDefaultAsync(Guid keepId, CancellationToken cancellationToken)
    {
        var priorDefaults = await _dbContext.SalaryStructures
            .Where(s => s.IsDefault && s.Id != keepId)
            .ToListAsync(cancellationToken);
        foreach (var s in priorDefaults)
            s.IsDefault = false;
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private async Task<Result<SalaryStructureDto>> BuildDtoResultAsync(Guid structureId, CancellationToken cancellationToken)
    {
        var structure = await _dbContext.SalaryStructures.AsNoTracking().FirstAsync(s => s.Id == structureId, cancellationToken);

        var links = await _dbContext.SalaryStructureComponents.AsNoTracking()
            .Where(x => x.SalaryStructureId == structureId)
            .OrderBy(x => x.ProcessingOrder)
            .ToListAsync(cancellationToken);

        var componentIds = links.Select(l => l.SalaryComponentId).Distinct().ToList();
        var components = componentIds.Count == 0
            ? new Dictionary<Guid, SalaryComponent>()
            : await _dbContext.SalaryComponents.AsNoTracking()
                .Where(c => componentIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, cancellationToken);

        var componentDtos = links.Select(l =>
        {
            components.TryGetValue(l.SalaryComponentId, out var c);
            var effectiveFormula = !string.IsNullOrWhiteSpace(l.OverrideFormula)
                ? l.OverrideFormula
                : c?.CalculationMethod == CalculationMethod.Formula ? c.FormulaExpression : null;
            return new SalaryStructureComponentDto
            {
                Id = l.Id,
                SalaryComponentId = l.SalaryComponentId,
                ComponentName = c?.Name ?? string.Empty,
                ComponentCode = c?.Code ?? string.Empty,
                ComponentType = c?.Type.ToString() ?? string.Empty,
                CalculationMethod = c?.CalculationMethod.ToString() ?? string.Empty,
                OverrideValue = l.OverrideValue,
                OverrideFormula = l.OverrideFormula,
                EffectiveValue = l.OverrideValue ?? c?.DefaultValue,
                EffectiveFormula = effectiveFormula,
                ProcessingOrder = l.ProcessingOrder,
                IsMandatory = l.IsMandatory,
            };
        }).ToList();

        return Result<SalaryStructureDto>.Success(new SalaryStructureDto
        {
            Id = structure.Id,
            Name = structure.Name,
            Code = structure.Code,
            Description = structure.Description,
            EffectiveFrom = structure.EffectiveFrom,
            IsDefault = structure.IsDefault,
            IsActive = structure.IsActive,
            Components = componentDtos,
            CreatedAt = structure.CreatedAt,
            UpdatedAt = structure.UpdatedAt,
        });
    }
}
