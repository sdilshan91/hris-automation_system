using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.LeaveTypes.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Leave type CRUD service (US-LV-001).
/// All queries are tenant-scoped via ITenantContext and EF global query filters.
/// </summary>
public sealed class LeaveTypeService : ILeaveTypeService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<LeaveTypeService> _logger;

    public LeaveTypeService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<LeaveTypeService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new leave type (AC-1). Validates name uniqueness case-insensitively (BR-1).
    /// </summary>
    public async Task<Result<LeaveTypeDto>> CreateAsync(
        CreateLeaveTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveTypeDto>.Failure("Tenant context is not resolved.", 400);

        // BR-1: case-insensitive name uniqueness within tenant
        var normalizedName = request.Name.Trim().ToLowerInvariant();
        var nameExists = await _dbContext.LeaveTypes
            .AnyAsync(lt => lt.Name.ToLower() == normalizedName, cancellationToken);

        if (nameExists)
            return Result<LeaveTypeDto>.Failure("A leave type with this name already exists.", 400);

        if (!Enum.TryParse<AccrualFrequency>(request.AccrualFrequency, true, out var accrualFrequency))
            return Result<LeaveTypeDto>.Failure("Invalid accrual frequency.", 400);

        if (!Enum.TryParse<LeaveTypeGender>(request.Gender, true, out var gender))
            return Result<LeaveTypeDto>.Failure("Invalid gender value.", 400);

        // Auto-assign display order if not specified
        var displayOrder = request.DisplayOrder;
        if (!displayOrder.HasValue)
        {
            var maxOrder = await _dbContext.LeaveTypes
                .MaxAsync(lt => (int?)lt.DisplayOrder, cancellationToken) ?? 0;
            displayOrder = maxOrder + 1;
        }

        var leaveType = new LeaveType
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Name = request.Name.Trim(),
            Code = request.Code?.Trim(),
            Color = request.Color?.Trim(),
            Description = request.Description?.Trim(),
            AnnualEntitlement = request.AnnualEntitlement,
            AccrualFrequency = accrualFrequency,
            CarryForwardLimit = request.CarryForwardLimit,
            CarryForwardExpiryMonths = request.CarryForwardExpiryMonths,
            ProbationEligible = request.ProbationEligible,
            DocumentsRequired = request.DocumentsRequired,
            DocumentDayThreshold = request.DocumentDayThreshold,
            Encashable = request.Encashable,
            MaxEncashDays = request.MaxEncashDays,
            HalfDayAllowed = request.HalfDayAllowed,
            HourlyAllowed = request.HourlyAllowed,
            Gender = gender,
            MaxConsecutiveDays = request.MaxConsecutiveDays,
            NegativeBalanceAllowed = request.NegativeBalanceAllowed,
            NegativeBalanceLimit = request.NegativeBalanceLimit,
            DisplayOrder = displayOrder.Value,
            IsActive = true,
            IsDeleted = false,
        };

        _dbContext.LeaveTypes.Add(leaveType);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Leave type created. Id={LeaveTypeId}, Name={LeaveTypeName}, TenantId={TenantId}, By={User}",
            leaveType.Id, leaveType.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result<LeaveTypeDto>.Success(ToDto(leaveType));
    }

    /// <summary>
    /// Updates a leave type (AC-2). Validates name uniqueness excluding self.
    /// </summary>
    public async Task<Result<LeaveTypeDto>> UpdateAsync(
        Guid leaveTypeId,
        UpdateLeaveTypeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveTypeDto>.Failure("Tenant context is not resolved.", 400);

        var leaveType = await _dbContext.LeaveTypes
            .FirstOrDefaultAsync(lt => lt.Id == leaveTypeId, cancellationToken);

        if (leaveType is null)
            return Result<LeaveTypeDto>.Failure("Leave type not found.", 404);

        // BR-1: case-insensitive name uniqueness (excluding self)
        var normalizedName = request.Name.Trim().ToLowerInvariant();
        var nameExists = await _dbContext.LeaveTypes
            .AnyAsync(lt => lt.Name.ToLower() == normalizedName && lt.Id != leaveTypeId, cancellationToken);

        if (nameExists)
            return Result<LeaveTypeDto>.Failure("A leave type with this name already exists.", 400);

        if (!Enum.TryParse<AccrualFrequency>(request.AccrualFrequency, true, out var accrualFrequency))
            return Result<LeaveTypeDto>.Failure("Invalid accrual frequency.", 400);

        if (!Enum.TryParse<LeaveTypeGender>(request.Gender, true, out var gender))
            return Result<LeaveTypeDto>.Failure("Invalid gender value.", 400);

        leaveType.Name = request.Name.Trim();
        leaveType.Code = request.Code?.Trim();
        leaveType.Color = request.Color?.Trim();
        leaveType.Description = request.Description?.Trim();
        leaveType.AnnualEntitlement = request.AnnualEntitlement;
        leaveType.AccrualFrequency = accrualFrequency;
        leaveType.CarryForwardLimit = request.CarryForwardLimit;
        leaveType.CarryForwardExpiryMonths = request.CarryForwardExpiryMonths;
        leaveType.ProbationEligible = request.ProbationEligible;
        leaveType.DocumentsRequired = request.DocumentsRequired;
        leaveType.DocumentDayThreshold = request.DocumentDayThreshold;
        leaveType.Encashable = request.Encashable;
        leaveType.MaxEncashDays = request.MaxEncashDays;
        leaveType.HalfDayAllowed = request.HalfDayAllowed;
        leaveType.HourlyAllowed = request.HourlyAllowed;
        leaveType.Gender = gender;
        leaveType.MaxConsecutiveDays = request.MaxConsecutiveDays;
        leaveType.NegativeBalanceAllowed = request.NegativeBalanceAllowed;
        leaveType.NegativeBalanceLimit = request.NegativeBalanceLimit;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Leave type updated. Id={LeaveTypeId}, Name={LeaveTypeName}, TenantId={TenantId}, By={User}",
            leaveType.Id, leaveType.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result<LeaveTypeDto>.Success(ToDto(leaveType));
    }

    /// <summary>
    /// Deactivates a leave type (AC-4, FR-5).
    /// Hides from application forms but retained for historical reporting.
    /// </summary>
    public async Task<Result> DeactivateAsync(
        Guid leaveTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var leaveType = await _dbContext.LeaveTypes
            .FirstOrDefaultAsync(lt => lt.Id == leaveTypeId, cancellationToken);

        if (leaveType is null)
            return Result.Failure("Leave type not found.", 404);

        if (!leaveType.IsActive)
            return Result.Failure("Leave type is already deactivated.", 400);

        leaveType.IsActive = false;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Leave type deactivated. Id={LeaveTypeId}, Name={LeaveTypeName}, TenantId={TenantId}, By={User}",
            leaveType.Id, leaveType.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    /// <summary>
    /// Reactivates a previously deactivated leave type.
    /// </summary>
    public async Task<Result> ReactivateAsync(
        Guid leaveTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var leaveType = await _dbContext.LeaveTypes
            .FirstOrDefaultAsync(lt => lt.Id == leaveTypeId, cancellationToken);

        if (leaveType is null)
            return Result.Failure("Leave type not found.", 404);

        if (leaveType.IsActive)
            return Result.Failure("Leave type is already active.", 400);

        leaveType.IsActive = true;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Leave type reactivated. Id={LeaveTypeId}, Name={LeaveTypeName}, TenantId={TenantId}, By={User}",
            leaveType.Id, leaveType.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    public async Task<Result<LeaveTypeDto>> GetByIdAsync(
        Guid leaveTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<LeaveTypeDto>.Failure("Tenant context is not resolved.", 400);

        var leaveType = await _dbContext.LeaveTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(lt => lt.Id == leaveTypeId, cancellationToken);

        if (leaveType is null)
            return Result<LeaveTypeDto>.Failure("Leave type not found.", 404);

        return Result<LeaveTypeDto>.Success(ToDto(leaveType));
    }

    public async Task<Result<IReadOnlyList<LeaveTypeDto>>> GetAllAsync(
        bool? activeOnly = null,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<LeaveTypeDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.LeaveTypes.AsNoTracking();

        if (activeOnly == true)
            query = query.Where(lt => lt.IsActive);

        var leaveTypes = await query
            .OrderBy(lt => lt.DisplayOrder)
            .ThenBy(lt => lt.Name)
            .ToListAsync(cancellationToken);

        var dtos = leaveTypes.Select(ToDto).ToList();
        return Result<IReadOnlyList<LeaveTypeDto>>.Success(dtos);
    }

    /// <summary>
    /// Reorders leave types by setting display_order from an ordered list of IDs (FR-3).
    /// </summary>
    public async Task<Result> ReorderAsync(
        IReadOnlyList<Guid> leaveTypeIds,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var leaveTypes = await _dbContext.LeaveTypes
            .ToListAsync(cancellationToken);

        if (leaveTypeIds.Count != leaveTypes.Count)
            return Result.Failure("The number of leave type IDs must match the number of leave types for this tenant.", 400);

        var leaveTypeMap = leaveTypes.ToDictionary(lt => lt.Id);

        for (var i = 0; i < leaveTypeIds.Count; i++)
        {
            if (!leaveTypeMap.TryGetValue(leaveTypeIds[i], out var lt))
                return Result.Failure($"Leave type with ID '{leaveTypeIds[i]}' not found.", 400);

            lt.DisplayOrder = i + 1;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Leave types reordered. Count={Count}, TenantId={TenantId}, By={User}",
            leaveTypeIds.Count, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    /// <summary>
    /// Seeds default leave types for a newly provisioned tenant (FR-4).
    /// Idempotent: skips if any leave types already exist for the tenant.
    /// </summary>
    public async Task<Result> SeedDefaultsForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // Check if any leave types already exist for this tenant
        // Use IgnoreQueryFilters to bypass tenant context (caller may be in system context)
        var hasExisting = await _dbContext.LeaveTypes
            .IgnoreQueryFilters()
            .AnyAsync(lt => lt.TenantId == tenantId && !lt.IsDeleted, cancellationToken);

        if (hasExisting)
        {
            _logger.LogInformation(
                "Skipping default leave type seeding — tenant {TenantId} already has leave types.", tenantId);
            return Result.Success();
        }

        var defaults = GetDefaultLeaveTypes(tenantId);

        _dbContext.LeaveTypes.AddRange(defaults);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded {Count} default leave types for tenant {TenantId}.",
            defaults.Count, tenantId);

        return Result.Success();
    }

    // -- Private helpers --

    private static LeaveTypeDto ToDto(LeaveType lt) => new()
    {
        Id = lt.Id,
        Name = lt.Name,
        Code = lt.Code,
        Color = lt.Color,
        Description = lt.Description,
        AnnualEntitlement = lt.AnnualEntitlement,
        AccrualFrequency = lt.AccrualFrequency.ToString(),
        CarryForwardLimit = lt.CarryForwardLimit,
        CarryForwardExpiryMonths = lt.CarryForwardExpiryMonths,
        ProbationEligible = lt.ProbationEligible,
        DocumentsRequired = lt.DocumentsRequired,
        DocumentDayThreshold = lt.DocumentDayThreshold,
        Encashable = lt.Encashable,
        MaxEncashDays = lt.MaxEncashDays,
        HalfDayAllowed = lt.HalfDayAllowed,
        HourlyAllowed = lt.HourlyAllowed,
        Gender = lt.Gender.ToString(),
        MaxConsecutiveDays = lt.MaxConsecutiveDays,
        NegativeBalanceAllowed = lt.NegativeBalanceAllowed,
        NegativeBalanceLimit = lt.NegativeBalanceLimit,
        DisplayOrder = lt.DisplayOrder,
        IsActive = lt.IsActive,
        CreatedAt = lt.CreatedAt,
        UpdatedAt = lt.UpdatedAt,
    };

    /// <summary>
    /// Returns the default set of leave types seeded for new tenants (FR-4).
    /// These are suggestions; tenants can modify or delete them.
    /// </summary>
    internal static IReadOnlyList<LeaveType> GetDefaultLeaveTypes(Guid tenantId)
    {
        var now = DateTime.UtcNow;
        return new List<LeaveType>
        {
            new()
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = "Annual Leave",
                Code = "AL",
                Color = "#4CAF50",
                Description = "Paid annual leave entitlement.",
                AnnualEntitlement = 14,
                AccrualFrequency = AccrualFrequency.Monthly,
                CarryForwardLimit = 5,
                CarryForwardExpiryMonths = 3,
                ProbationEligible = false,
                DocumentsRequired = false,
                Encashable = true,
                MaxEncashDays = 5,
                HalfDayAllowed = true,
                Gender = LeaveTypeGender.All,
                MaxConsecutiveDays = 14,
                DisplayOrder = 1,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "system-seed",
            },
            new()
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = "Sick Leave",
                Code = "SL",
                Color = "#F44336",
                Description = "Paid sick leave. Medical certificate required for absences exceeding 2 days.",
                AnnualEntitlement = 7,
                AccrualFrequency = AccrualFrequency.Upfront,
                ProbationEligible = true,
                DocumentsRequired = true,
                DocumentDayThreshold = 2,
                HalfDayAllowed = true,
                Gender = LeaveTypeGender.All,
                DisplayOrder = 2,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "system-seed",
            },
            new()
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = "Casual Leave",
                Code = "CL",
                Color = "#2196F3",
                Description = "Casual leave for personal matters.",
                AnnualEntitlement = 7,
                AccrualFrequency = AccrualFrequency.Upfront,
                ProbationEligible = true,
                HalfDayAllowed = true,
                Gender = LeaveTypeGender.All,
                MaxConsecutiveDays = 3,
                DisplayOrder = 3,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "system-seed",
            },
            new()
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = "Maternity Leave",
                Code = "MAT",
                Color = "#E91E63",
                Description = "Paid maternity leave.",
                AnnualEntitlement = 84,
                AccrualFrequency = AccrualFrequency.Upfront,
                ProbationEligible = false,
                DocumentsRequired = true,
                DocumentDayThreshold = 1,
                Gender = LeaveTypeGender.Female,
                DisplayOrder = 4,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "system-seed",
            },
            new()
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = "Paternity Leave",
                Code = "PAT",
                Color = "#3F51B5",
                Description = "Paid paternity leave.",
                AnnualEntitlement = 5,
                AccrualFrequency = AccrualFrequency.Upfront,
                ProbationEligible = false,
                Gender = LeaveTypeGender.Male,
                DisplayOrder = 5,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "system-seed",
            },
            new()
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = "Bereavement Leave",
                Code = "BL",
                Color = "#607D8B",
                Description = "Leave for the death of an immediate family member.",
                AnnualEntitlement = 3,
                AccrualFrequency = AccrualFrequency.Upfront,
                ProbationEligible = true,
                Gender = LeaveTypeGender.All,
                MaxConsecutiveDays = 5,
                DisplayOrder = 6,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "system-seed",
            },
            new()
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = "Unpaid Leave",
                Code = "UL",
                Color = "#9E9E9E",
                Description = "Unpaid leave when paid entitlement is exhausted.",
                AnnualEntitlement = 0,
                AccrualFrequency = AccrualFrequency.Upfront,
                ProbationEligible = true,
                NegativeBalanceAllowed = true,
                NegativeBalanceLimit = 30,
                Gender = LeaveTypeGender.All,
                DisplayOrder = 7,
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "system-seed",
            },
        };
    }
}
