using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Attendance.DTOs;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Shift management + assignment service (US-ATT-005). All queries are tenant-scoped via
/// ITenantContext and the EF global query filters. No Redis cache (NFR-4 deferred — no Redis in this
/// codebase) and no PostgreSQL RLS (NFR-3 — tenant isolation is via EF filters + TenantInterceptor),
/// consistent with the rest of the Attendance module.
/// </summary>
public sealed class ShiftService : IShiftService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<ShiftService> _logger;

    public ShiftService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<ShiftService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyList<ShiftDto>>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<ShiftDto>>.Failure("Tenant context is not resolved.", 400);

        var shifts = await _dbContext.Shifts
            .AsNoTracking()
            .Include(s => s.RotationSteps)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var counts = await GetAssignedCountsAsync(shifts.Select(s => s.Id).ToList(), cancellationToken);

        var dtos = shifts.Select(s => ToDto(s, counts.GetValueOrDefault(s.Id, 0))).ToList();
        return Result<IReadOnlyList<ShiftDto>>.Success(dtos);
    }

    public async Task<Result<ShiftDto>> CreateAsync(
        ShiftRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ShiftDto>.Failure("Tenant context is not resolved.", 400);

        var nameExists = await _dbContext.Shifts
            .AnyAsync(s => s.Name == request.Name, cancellationToken);
        if (nameExists)
            return Result<ShiftDto>.Failure(
                "A shift with this name already exists.", 409, "duplicate_name");

        var rotationError = await ValidateRotationReferencesAsync(request, cancellationToken);
        if (rotationError is not null)
            return Result<ShiftDto>.Failure(rotationError, 400, "invalid_rotation");

        var shift = new Shift
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Name = request.Name.Trim(),
            Type = request.Type,
            StartTime = ParseTime(request.StartTime),
            EndTime = ParseTime(request.EndTime),
            BreakDurationMinutes = request.BreakDurationMinutes,
            GracePeriodMinutes = request.GracePeriodMinutes,
            MinimumHours = request.MinimumHours,
            WorkingDays = request.WorkingDays.Distinct().OrderBy(d => d).ToList(),
            IsDefault = false,
            IsActive = true,
        };

        ApplyRotation(shift, request.Rotation);

        _dbContext.Shifts.Add(shift);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Shift created. Id={ShiftId}, Name={Name}, Type={Type}, TenantId={TenantId}, By={User}",
            shift.Id, shift.Name, shift.Type, _tenantContext.TenantId, _currentUser.Email);

        return Result<ShiftDto>.Success(ToDto(shift, 0));
    }

    public async Task<Result<ShiftDto>> UpdateAsync(
        Guid shiftId, ShiftRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ShiftDto>.Failure("Tenant context is not resolved.", 400);

        var shift = await _dbContext.Shifts
            .Include(s => s.RotationSteps)
            .FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken);
        if (shift is null)
            return Result<ShiftDto>.Failure("Shift not found.", 404);

        var nameExists = await _dbContext.Shifts
            .AnyAsync(s => s.Name == request.Name && s.Id != shiftId, cancellationToken);
        if (nameExists)
            return Result<ShiftDto>.Failure(
                "A shift with this name already exists.", 409, "duplicate_name");

        var rotationError = await ValidateRotationReferencesAsync(request, cancellationToken, shiftId);
        if (rotationError is not null)
            return Result<ShiftDto>.Failure(rotationError, 400, "invalid_rotation");

        shift.Name = request.Name.Trim();
        shift.Type = request.Type;
        shift.StartTime = ParseTime(request.StartTime);
        shift.EndTime = ParseTime(request.EndTime);
        shift.BreakDurationMinutes = request.BreakDurationMinutes;
        shift.GracePeriodMinutes = request.GracePeriodMinutes;
        shift.MinimumHours = request.MinimumHours;
        shift.WorkingDays = request.WorkingDays.Distinct().OrderBy(d => d).ToList();

        // Replace rotation steps wholesale (soft-delete the old ones to honour the unique/order index).
        foreach (var existing in shift.RotationSteps)
            existing.IsDeleted = true;
        shift.RotationSteps.Clear();
        ApplyRotation(shift, request.Rotation);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var count = (await GetAssignedCountsAsync(new List<Guid> { shift.Id }, cancellationToken))
            .GetValueOrDefault(shift.Id, 0);

        _logger.LogInformation(
            "Shift updated. Id={ShiftId}, Name={Name}, TenantId={TenantId}, By={User}",
            shift.Id, shift.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result<ShiftDto>.Success(ToDto(shift, count));
    }

    public async Task<Result> DeleteAsync(
        Guid shiftId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var shift = await _dbContext.Shifts
            .Include(s => s.RotationSteps)
            .FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken);
        if (shift is null)
            return Result.Failure("Shift not found.", 404);

        // AC-4 / FR-6: block deletion when active assignments exist (exact message).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var assignedCount = await _dbContext.EmployeeShifts
            .CountAsync(
                es => es.ShiftId == shiftId
                    && es.EffectiveFrom <= today
                    && (es.EffectiveTo == null || es.EffectiveTo >= today),
                cancellationToken);

        if (assignedCount > 0)
            return Result.Failure(
                $"This shift is assigned to {assignedCount} employees. Please reassign them before deleting.",
                409, "shift_in_use");

        shift.IsDeleted = true;
        foreach (var step in shift.RotationSteps)
            step.IsDeleted = true;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Shift deleted. Id={ShiftId}, TenantId={TenantId}, By={User}",
            shift.Id, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    public async Task<Result<ShiftDto>> CloneAsync(
        Guid shiftId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ShiftDto>.Failure("Tenant context is not resolved.", 400);

        var source = await _dbContext.Shifts
            .AsNoTracking()
            .Include(s => s.RotationSteps)
            .FirstOrDefaultAsync(s => s.Id == shiftId, cancellationToken);
        if (source is null)
            return Result<ShiftDto>.Failure("Shift not found.", 404);

        // FR-8: derive a unique "Copy of {orig}" name (append a counter if needed).
        var cloneName = await DeriveCloneNameAsync(source.Name, cancellationToken);

        var clone = new Shift
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Name = cloneName,
            Type = source.Type,
            StartTime = source.StartTime,
            EndTime = source.EndTime,
            BreakDurationMinutes = source.BreakDurationMinutes,
            GracePeriodMinutes = source.GracePeriodMinutes,
            MinimumHours = source.MinimumHours,
            WorkingDays = new List<int>(source.WorkingDays),
            IsDefault = false,
            IsActive = true,
            RotationCycleLengthDays = source.RotationCycleLengthDays,
            RotationReferenceStartDate = source.RotationReferenceStartDate,
        };

        foreach (var step in source.RotationSteps.OrderBy(s => s.Order))
        {
            clone.RotationSteps.Add(new ShiftRotationStep
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                ShiftId = clone.Id,
                Order = step.Order,
                StepShiftId = step.StepShiftId,
                DurationDays = step.DurationDays,
            });
        }

        _dbContext.Shifts.Add(clone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Shift cloned. SourceId={SourceId}, CloneId={CloneId}, TenantId={TenantId}, By={User}",
            source.Id, clone.Id, _tenantContext.TenantId, _currentUser.Email);

        return Result<ShiftDto>.Success(ToDto(clone, 0));
    }

    public async Task<Result<AssignmentResultDto>> AssignAsync(
        Guid shiftId, IReadOnlyList<Guid> employeeIds, DateOnly effectiveFrom,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<AssignmentResultDto>.Failure("Tenant context is not resolved.", 400);

        var shiftExists = await _dbContext.Shifts.AnyAsync(s => s.Id == shiftId, cancellationToken);
        if (!shiftExists)
            return Result<AssignmentResultDto>.Failure("Shift not found.", 404);

        var distinctIds = employeeIds.Distinct().ToList();

        // Tenant-scoped existence check (the global filter prevents cross-tenant assignment).
        var validEmployeeIds = await _dbContext.Employees
            .Where(e => distinctIds.Contains(e.Id))
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var missing = distinctIds.Except(validEmployeeIds).ToList();
        if (missing.Count > 0)
            return Result<AssignmentResultDto>.Failure(
                $"{missing.Count} of the supplied employees were not found in this tenant.",
                400, "employee_not_found");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // NFR-2: bulk up to 500 — load all currently-open assignments for the target employees once.
        var openAssignments = await _dbContext.EmployeeShifts
            .Where(es => validEmployeeIds.Contains(es.EmployeeId) && es.EffectiveTo == null)
            .ToListAsync(cancellationToken);
        var openByEmployee = openAssignments
            .GroupBy(es => es.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(es => es.EffectiveFrom).First());

        var newAssignments = new List<EmployeeShift>(validEmployeeIds.Count);

        foreach (var employeeId in validEmployeeIds)
        {
            // BR-2 / AC-3: close the current open assignment so there is no overlap. If the new
            // assignment is effective today/in the past, the current one ends the day before
            // effectiveFrom. If effectiveFrom is in the future, the current assignment stays active
            // until then (its effective_to is still set to the day before the new one starts).
            if (openByEmployee.TryGetValue(employeeId, out var current)
                && current.EffectiveFrom < effectiveFrom)
            {
                current.EffectiveTo = effectiveFrom.AddDays(-1);
            }
            else if (openByEmployee.TryGetValue(employeeId, out var sameDay)
                && sameDay.EffectiveFrom >= effectiveFrom)
            {
                // An existing open assignment starts on/after the new effective date — supersede it
                // entirely (soft-delete) so we never create an overlapping active pair (BR-2).
                sameDay.IsDeleted = true;
            }

            var assignment = new EmployeeShift
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                EmployeeId = employeeId,
                ShiftId = shiftId,
                EffectiveFrom = effectiveFrom,
                EffectiveTo = null,
            };
            newAssignments.Add(assignment);
        }

        _dbContext.EmployeeShifts.AddRange(newAssignments);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Shift assigned. ShiftId={ShiftId}, Count={Count}, EffectiveFrom={EffectiveFrom}, TenantId={TenantId}, By={User}",
            shiftId, newAssignments.Count, effectiveFrom, _tenantContext.TenantId, _currentUser.Email);

        return Result<AssignmentResultDto>.Success(new AssignmentResultDto
        {
            AssignedCount = newAssignments.Count,
            EmployeeShiftIds = newAssignments.Select(a => a.Id).ToList(),
        });
    }

    public async Task<Result<ResolvedShiftDto>> ResolveForEmployeeAsync(
        Guid employeeId, DateOnly date, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<ResolvedShiftDto>.Failure("Tenant context is not resolved.", 400);

        var employeeExists = await _dbContext.Employees.AnyAsync(e => e.Id == employeeId, cancellationToken);
        if (!employeeExists)
            return Result<ResolvedShiftDto>.Failure("Employee not found.", 404);

        // FR-7: the assignment effective on the requested date (most recent effective_from wins).
        var assignment = await _dbContext.EmployeeShifts
            .AsNoTracking()
            .Where(es => es.EmployeeId == employeeId
                && es.EffectiveFrom <= date
                && (es.EffectiveTo == null || es.EffectiveTo >= date))
            .OrderByDescending(es => es.EffectiveFrom)
            .FirstOrDefaultAsync(cancellationToken);

        Shift? shift = null;
        DateOnly? effectiveFrom = null;
        DateOnly? effectiveTo = null;

        if (assignment is not null)
        {
            shift = await _dbContext.Shifts
                .AsNoTracking()
                .Include(s => s.RotationSteps)
                .FirstOrDefaultAsync(s => s.Id == assignment.ShiftId, cancellationToken);
            effectiveFrom = assignment.EffectiveFrom;
            effectiveTo = assignment.EffectiveTo;
        }

        // FR-5: fall back to the tenant default shift when there is no assignment.
        shift ??= await _dbContext.Shifts
            .AsNoTracking()
            .Include(s => s.RotationSteps)
            .FirstOrDefaultAsync(s => s.IsDefault, cancellationToken);

        if (shift is null)
            return Result<ResolvedShiftDto>.Failure(
                "No shift assigned and no tenant default shift configured.", 404, "no_shift");

        // FR-7: for ROTATING, resolve to the concrete step shift applicable on the date.
        var resolved = await ResolveRotationStepAsync(shift, date, cancellationToken);
        var count = (await GetAssignedCountsAsync(new List<Guid> { resolved.Id }, cancellationToken))
            .GetValueOrDefault(resolved.Id, 0);

        var dto = ToDto(resolved, count);
        return Result<ResolvedShiftDto>.Success(new ResolvedShiftDto
        {
            Id = dto.Id,
            Name = dto.Name,
            Type = dto.Type,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            BreakDurationMinutes = dto.BreakDurationMinutes,
            GracePeriodMinutes = dto.GracePeriodMinutes,
            MinimumHours = dto.MinimumHours,
            WorkingDays = dto.WorkingDays,
            IsDefault = dto.IsDefault,
            IsActive = dto.IsActive,
            AssignedEmployeeCount = dto.AssignedEmployeeCount,
            Rotation = dto.Rotation,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            ResolvedForDate = date,
        });
    }

    // ── Rotation resolution (FR-7/AC-5) ────────────────────────────────

    /// <summary>
    /// For a ROTATING shift, returns the concrete step shift applicable on <paramref name="date"/>;
    /// for SINGLE/FLEXIBLE returns the shift unchanged. The day-index is the number of days since the
    /// rotation reference date, reduced modulo the cycle length; steps are consumed in order by their
    /// duration. If the rotation is incomplete the original shift is returned (graceful fallback).
    /// </summary>
    private async Task<Shift> ResolveRotationStepAsync(
        Shift shift, DateOnly date, CancellationToken cancellationToken)
    {
        if (shift.Type != ShiftType.Rotating
            || shift.RotationReferenceStartDate is null
            || shift.RotationCycleLengthDays is null or <= 0
            || shift.RotationSteps.Count == 0)
        {
            return shift;
        }

        var cycle = shift.RotationCycleLengthDays.Value;
        var dayOffset = date.DayNumber - shift.RotationReferenceStartDate.Value.DayNumber;
        // Positive modulo so dates before the reference still resolve sensibly.
        var dayInCycle = ((dayOffset % cycle) + cycle) % cycle;

        Guid? stepShiftId = null;
        var cursor = 0;
        foreach (var step in shift.RotationSteps.OrderBy(s => s.Order))
        {
            if (dayInCycle < cursor + step.DurationDays)
            {
                stepShiftId = step.StepShiftId;
                break;
            }
            cursor += step.DurationDays;
        }

        if (stepShiftId is null)
            return shift;

        var stepShift = await _dbContext.Shifts
            .AsNoTracking()
            .Include(s => s.RotationSteps)
            .FirstOrDefaultAsync(s => s.Id == stepShiftId.Value, cancellationToken);

        return stepShift ?? shift;
    }

    // ── Validation helpers ─────────────────────────────────────────────

    /// <summary>
    /// Validates that ROTATING rotation steps reference existing, tenant-visible, non-rotating shifts.
    /// Returns an error message or null when valid. (Shape/structure rules live in the FluentValidator.)
    /// </summary>
    private async Task<string?> ValidateRotationReferencesAsync(
        ShiftRequest request, CancellationToken cancellationToken, Guid? selfId = null)
    {
        if (request.Type != ShiftType.Rotating || request.Rotation is null)
            return null;

        var stepShiftIds = request.Rotation.Steps.Select(s => s.ShiftId).Distinct().ToList();
        if (stepShiftIds.Count == 0)
            return "A rotating shift must reference at least one step shift.";

        if (selfId is not null && stepShiftIds.Contains(selfId.Value))
            return "A rotating shift cannot reference itself as a step.";

        var found = await _dbContext.Shifts
            .Where(s => stepShiftIds.Contains(s.Id) && s.Type != ShiftType.Rotating)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        var bad = stepShiftIds.Except(found).ToList();
        if (bad.Count > 0)
            return "One or more rotation steps reference a shift that does not exist or is itself rotating.";

        return null;
    }

    private async Task<string> DeriveCloneNameAsync(string originalName, CancellationToken cancellationToken)
    {
        var baseName = $"Copy of {originalName}";
        var existing = await _dbContext.Shifts
            .Where(s => s.Name == baseName || s.Name.StartsWith(baseName + " ("))
            .Select(s => s.Name)
            .ToListAsync(cancellationToken);

        if (!existing.Contains(baseName))
            return Truncate(baseName);

        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!existing.Contains(candidate))
                return Truncate(candidate);
        }
    }

    private static string Truncate(string name) => name.Length <= 100 ? name : name[..100];

    private async Task<Dictionary<Guid, int>> GetAssignedCountsAsync(
        List<Guid> shiftIds, CancellationToken cancellationToken)
    {
        if (shiftIds.Count == 0)
            return new Dictionary<Guid, int>();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return await _dbContext.EmployeeShifts
            .Where(es => shiftIds.Contains(es.ShiftId)
                && es.EffectiveFrom <= today
                && (es.EffectiveTo == null || es.EffectiveTo >= today))
            .GroupBy(es => es.ShiftId)
            .Select(g => new { ShiftId = g.Key, Count = g.Select(es => es.EmployeeId).Distinct().Count() })
            .ToDictionaryAsync(g => g.ShiftId, g => g.Count, cancellationToken);
    }

    private static void ApplyRotation(Shift shift, RotationRequest? rotation)
    {
        if (shift.Type != ShiftType.Rotating || rotation is null)
        {
            shift.RotationCycleLengthDays = null;
            shift.RotationReferenceStartDate = null;
            return;
        }

        shift.RotationCycleLengthDays = rotation.CycleLengthDays;
        shift.RotationReferenceStartDate = rotation.ReferenceStartDate;

        foreach (var step in rotation.Steps.OrderBy(s => s.Order))
        {
            shift.RotationSteps.Add(new ShiftRotationStep
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = shift.TenantId,
                ShiftId = shift.Id,
                Order = step.Order,
                StepShiftId = step.ShiftId,
                DurationDays = step.DurationDays,
            });
        }
    }

    private static TimeOnly? ParseTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return TimeOnly.ParseExact(value, "HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ShiftDto ToDto(Shift s, int assignedCount)
    {
        RotationDto? rotation = null;
        if (s.Type == ShiftType.Rotating && s.RotationCycleLengthDays is not null
            && s.RotationReferenceStartDate is not null)
        {
            rotation = new RotationDto
            {
                CycleLengthDays = s.RotationCycleLengthDays.Value,
                ReferenceStartDate = s.RotationReferenceStartDate.Value,
                Steps = s.RotationSteps
                    .Where(rs => !rs.IsDeleted)
                    .OrderBy(rs => rs.Order)
                    .Select(rs => new RotationStepDto
                    {
                        Order = rs.Order,
                        ShiftId = rs.StepShiftId,
                        DurationDays = rs.DurationDays,
                    })
                    .ToList(),
            };
        }

        return new ShiftDto
        {
            Id = s.Id,
            Name = s.Name,
            Type = s.Type,
            StartTime = s.StartTime?.ToString("HH:mm"),
            EndTime = s.EndTime?.ToString("HH:mm"),
            BreakDurationMinutes = s.BreakDurationMinutes,
            GracePeriodMinutes = s.GracePeriodMinutes,
            MinimumHours = s.MinimumHours,
            WorkingDays = s.WorkingDays,
            IsDefault = s.IsDefault,
            IsActive = s.IsActive,
            AssignedEmployeeCount = assignedCount,
            Rotation = rotation,
        };
    }
}
