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
/// Salary-component CRUD service (US-PAY-001). All queries are tenant-scoped via ITenantContext + the EF
/// global query filter (AC-6 cross-tenant isolation). Enforces unique code per tenant (BR-2), formula
/// syntax + circular-ref validation (BR-6), and the delete-guard when a component is linked to a
/// structure (AC-5). Audit (CreatedBy/UpdatedBy) is stamped by the AuditInterceptor on SaveChanges.
/// </summary>
public sealed class SalaryComponentService : ISalaryComponentService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<SalaryComponentService> _logger;

    private const int MaxPageSize = 100;

    public SalaryComponentService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<SalaryComponentService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<SalaryComponentDto>> CreateAsync(SalaryComponentInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryComponentDto>.Failure("Tenant context is not resolved.", 400);

        var code = NormalizeCode(input.Code);

        var validation = ValidateCalculation(input.CalculationMethod, input.DefaultValue, input.FormulaExpression);
        if (validation is not null)
            return Result<SalaryComponentDto>.Failure(validation.Value.error, 400, validation.Value.code);

        // BR-2: unique code per tenant (global filter scopes to tenant; partial unique index is the DB guard).
        if (await _dbContext.SalaryComponents.AnyAsync(c => c.Code == code, cancellationToken))
            return Result<SalaryComponentDto>.Failure(
                $"A salary component with code '{code}' already exists.", 409, "duplicate_code");

        var component = new SalaryComponent
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Name = input.Name.Trim(),
            Code = code,
            Type = input.Type,
            CalculationMethod = input.CalculationMethod,
            DefaultValue = input.CalculationMethod == CalculationMethod.Formula ? null : input.DefaultValue,
            FormulaExpression = input.CalculationMethod == CalculationMethod.Formula
                ? input.FormulaExpression!.Trim() : null,
            IsTaxable = input.IsTaxable,
            IsStatutory = input.IsStatutory,
            IsActive = input.IsActive,
            ProcessingOrder = input.ProcessingOrder,
            IsDeleted = false,
        };

        _dbContext.SalaryComponents.Add(component);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Salary component created. Id={Id}, Code={Code}, TenantId={TenantId}, By={User}",
            component.Id, component.Code, _tenantContext.TenantId, _currentUser.Email);

        return Result<SalaryComponentDto>.Success(ToDto(component));
    }

    public async Task<Result<SalaryComponentDto>> UpdateAsync(Guid componentId, SalaryComponentInput input, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryComponentDto>.Failure("Tenant context is not resolved.", 400);

        var component = await _dbContext.SalaryComponents.FirstOrDefaultAsync(c => c.Id == componentId, cancellationToken);
        if (component is null)
            return Result<SalaryComponentDto>.Failure("Salary component not found.", 404);

        var code = NormalizeCode(input.Code);

        var validation = ValidateCalculation(input.CalculationMethod, input.DefaultValue, input.FormulaExpression);
        if (validation is not null)
            return Result<SalaryComponentDto>.Failure(validation.Value.error, 400, validation.Value.code);

        // BR-2: unique code per tenant excluding self.
        if (await _dbContext.SalaryComponents.AnyAsync(c => c.Code == code && c.Id != componentId, cancellationToken))
            return Result<SalaryComponentDto>.Failure(
                $"A salary component with code '{code}' already exists.", 409, "duplicate_code");

        component.Name = input.Name.Trim();
        component.Code = code;
        component.Type = input.Type;
        component.CalculationMethod = input.CalculationMethod;
        component.DefaultValue = input.CalculationMethod == CalculationMethod.Formula ? null : input.DefaultValue;
        component.FormulaExpression = input.CalculationMethod == CalculationMethod.Formula
            ? input.FormulaExpression!.Trim() : null;
        component.IsTaxable = input.IsTaxable;
        component.IsStatutory = input.IsStatutory;
        component.IsActive = input.IsActive;
        component.ProcessingOrder = input.ProcessingOrder;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Salary component updated. Id={Id}, Code={Code}, TenantId={TenantId}, By={User}",
            component.Id, component.Code, _tenantContext.TenantId, _currentUser.Email);

        return Result<SalaryComponentDto>.Success(ToDto(component));
    }

    public async Task<Result<SalaryComponentDto>> GetByIdAsync(Guid componentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<SalaryComponentDto>.Failure("Tenant context is not resolved.", 400);

        var component = await _dbContext.SalaryComponents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == componentId, cancellationToken);

        return component is null
            ? Result<SalaryComponentDto>.Failure("Salary component not found.", 404)
            : Result<SalaryComponentDto>.Success(ToDto(component));
    }

    public async Task<Result<PagedResult<SalaryComponentListItemDto>>> ListAsync(
        SalaryComponentType? type, bool? isActive, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<PagedResult<SalaryComponentListItemDto>>.Failure("Tenant context is not resolved.", 400);

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxPageSize ? Math.Clamp(pageSize, 1, MaxPageSize) : pageSize;

        var query = _dbContext.SalaryComponents.AsNoTracking();
        if (type.HasValue)
            query = query.Where(c => c.Type == type.Value);
        if (isActive.HasValue)
            query = query.Where(c => c.IsActive == isActive.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(term) || c.Code.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var components = await query
            .OrderBy(c => c.ProcessingOrder).ThenBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = components.Select(c => new SalaryComponentListItemDto
        {
            Id = c.Id,
            Name = c.Name,
            Code = c.Code,
            Type = c.Type,
            TypeName = c.Type.ToString(),
            CalculationMethod = c.CalculationMethod,
            CalculationMethodName = c.CalculationMethod.ToString(),
            DefaultValue = c.DefaultValue,
            IsTaxable = c.IsTaxable,
            IsStatutory = c.IsStatutory,
            IsActive = c.IsActive,
            ProcessingOrder = c.ProcessingOrder,
        }).ToList();

        return Result<PagedResult<SalaryComponentListItemDto>>.Success(new PagedResult<SalaryComponentListItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        });
    }

    public async Task<Result> DeleteAsync(Guid componentId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var component = await _dbContext.SalaryComponents.FirstOrDefaultAsync(c => c.Id == componentId, cancellationToken);
        if (component is null)
            return Result.Failure("Salary component not found.", 404);

        // AC-5: a component linked to any salary structure cannot be deleted; report the count.
        var inUseCount = await _dbContext.SalaryStructureComponents
            .CountAsync(x => x.SalaryComponentId == componentId, cancellationToken);
        if (inUseCount > 0)
            return Result.Failure(
                $"This component is used by {inUseCount} salary structure(s) and cannot be deleted. Remove the links first.",
                409, "component_in_use");

        component.IsDeleted = true;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Salary component soft-deleted. Id={Id}, Code={Code}, TenantId={TenantId}, By={User}",
            component.Id, component.Code, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Validates the calculation-method-specific fields. For Formula, the syntax is checked here (BR-6
    /// syntax half); circular-reference detection across components is a structure concern (it needs the
    /// other components' codes) handled by the structure service.
    /// </summary>
    private static (string error, string code)? ValidateCalculation(
        CalculationMethod method, decimal? defaultValue, string? formula)
    {
        if (method == CalculationMethod.Formula)
        {
            var error = SalaryFormula.Validate(formula);
            if (error is not null)
                return ($"Invalid formula: {error}", "invalid_formula");
            return null;
        }

        if (defaultValue is null)
            return ("A default value is required for non-formula calculation methods.", "default_value_required");

        if (method is CalculationMethod.PercentageOfBasic or CalculationMethod.PercentageOfGross
            && (defaultValue < 0 || defaultValue > 100))
            return ("A percentage value must be between 0 and 100.", "invalid_percentage");

        return null;
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static SalaryComponentDto ToDto(SalaryComponent c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Code = c.Code,
        Type = c.Type,
        TypeName = c.Type.ToString(),
        CalculationMethod = c.CalculationMethod,
        CalculationMethodName = c.CalculationMethod.ToString(),
        DefaultValue = c.DefaultValue,
        FormulaExpression = c.FormulaExpression,
        IsTaxable = c.IsTaxable,
        IsStatutory = c.IsStatutory,
        IsActive = c.IsActive,
        ProcessingOrder = c.ProcessingOrder,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };
}
