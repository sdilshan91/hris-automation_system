using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using HRM.Application.Common.Interfaces;
using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;
using HRM.Domain.Entities;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Holiday calendar service (US-LV-007). CRUD, range/list queries, CSV import,
/// recurrence generation, and tenant seeding. All queries are tenant-scoped via
/// ITenantContext and EF global query filters.
///
/// DEFERRALS (seams only — see vault "Holiday Calendar (US-LV-007)"):
///   - Redis caching of the holiday list (NFR-1) — read DB directly, consistent with the
///     module-wide deferred-Redis decision. TODO(redis-holiday-cache).
///   - Tenant onboarding seeding wiring (FR-5) — SeedHolidaysForTenantAsync is provided but
///     left UNWIRED. TODO(onboarding).
///   - Payroll-period lock on delete (BR-4) — soft-deactivate only; no payroll module exists.
///     TODO(payroll).
///   - Optional-holiday-as-leave-type accounting (BR-3) — out of scope. TODO(optional-holiday).
/// </summary>
public sealed class HolidayService : IHolidayService
{
    private readonly AppDbContext _dbContext;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<HolidayService> _logger;

    /// <summary>NFR-3: CSV import handles up to 100 rows.</summary>
    internal const int MaxImportRows = 100;

    public HolidayService(
        AppDbContext dbContext,
        ITenantContext tenantContext,
        ICurrentUser currentUser,
        ILogger<HolidayService> logger)
    {
        _dbContext = dbContext;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════════════════
    //  Create (FR-1, AC-1, BR-1)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<HolidayDto>> CreateAsync(
        CreateHolidayRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<HolidayDto>.Failure("Tenant context is not resolved.", 400);

        if (!Enum.TryParse<HolidayType>(request.Type, true, out var type))
            return Result<HolidayDto>.Failure("Invalid holiday type.", 400);

        if (request.LocationId.HasValue)
        {
            var locationExists = await _dbContext.Locations
                .AnyAsync(l => l.Id == request.LocationId.Value, cancellationToken);
            if (!locationExists)
                return Result<HolidayDto>.Failure("Location not found.", 404);
        }

        // BR-1: a holiday date must be unique per tenant + location.
        if (await DuplicateExistsAsync(request.Date, request.LocationId, null, cancellationToken))
            return Result<HolidayDto>.Failure(
                BuildDuplicateMessage(request.Date, request.LocationId), 400);

        var holiday = new Holiday
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = _tenantContext.TenantId,
            Name = request.Name.Trim(),
            Date = request.Date,
            Type = type,
            LocationId = request.LocationId,
            Description = request.Description?.Trim(),
            IsRecurring = request.IsRecurring,
            IsActive = true,
            IsDeleted = false,
        };

        _dbContext.Holidays.Add(holiday);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Holiday created. Id={HolidayId}, Name={Name}, Date={Date}, LocationId={LocationId}, TenantId={TenantId}, By={User}",
            holiday.Id, holiday.Name, holiday.Date, holiday.LocationId, _tenantContext.TenantId, _currentUser.Email);

        return Result<HolidayDto>.Success(await ToDtoWithLocationAsync(holiday, cancellationToken));
    }

    // ══════════════════════════════════════════════════════════════
    //  Update (FR-1)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<HolidayDto>> UpdateAsync(
        Guid holidayId, UpdateHolidayRequest request, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<HolidayDto>.Failure("Tenant context is not resolved.", 400);

        var holiday = await _dbContext.Holidays
            .FirstOrDefaultAsync(h => h.Id == holidayId, cancellationToken);
        if (holiday is null)
            return Result<HolidayDto>.Failure("Holiday not found.", 404);

        if (!Enum.TryParse<HolidayType>(request.Type, true, out var type))
            return Result<HolidayDto>.Failure("Invalid holiday type.", 400);

        if (request.LocationId.HasValue)
        {
            var locationExists = await _dbContext.Locations
                .AnyAsync(l => l.Id == request.LocationId.Value, cancellationToken);
            if (!locationExists)
                return Result<HolidayDto>.Failure("Location not found.", 404);
        }

        // BR-1: duplicate check excluding self.
        if (await DuplicateExistsAsync(request.Date, request.LocationId, holidayId, cancellationToken))
            return Result<HolidayDto>.Failure(
                BuildDuplicateMessage(request.Date, request.LocationId), 400);

        holiday.Name = request.Name.Trim();
        holiday.Date = request.Date;
        holiday.Type = type;
        holiday.LocationId = request.LocationId;
        holiday.Description = request.Description?.Trim();
        holiday.IsRecurring = request.IsRecurring;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Holiday updated. Id={HolidayId}, Name={Name}, TenantId={TenantId}, By={User}",
            holiday.Id, holiday.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result<HolidayDto>.Success(await ToDtoWithLocationAsync(holiday, cancellationToken));
    }

    // ══════════════════════════════════════════════════════════════
    //  Deactivate (BR-4)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result> DeactivateAsync(Guid holidayId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result.Failure("Tenant context is not resolved.", 400);

        var holiday = await _dbContext.Holidays
            .FirstOrDefaultAsync(h => h.Id == holidayId, cancellationToken);
        if (holiday is null)
            return Result.Failure("Holiday not found.", 404);

        if (!holiday.IsActive)
            return Result.Failure("Holiday is already deactivated.", 400);

        // BR-4: holidays falling within a finalized payroll period cannot be hard-deleted; they
        // can only be deactivated. No payroll module exists yet, so we always soft-deactivate.
        // TODO(payroll): once payroll periods exist, block hard-delete inside a locked period and
        //   permit deactivation as the safe alternative.
        holiday.IsActive = false;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Holiday deactivated. Id={HolidayId}, Name={Name}, TenantId={TenantId}, By={User}",
            holiday.Id, holiday.Name, _tenantContext.TenantId, _currentUser.Email);

        return Result.Success();
    }

    // ══════════════════════════════════════════════════════════════
    //  Queries (FR-6, AC-4)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<HolidayDto>> GetByIdAsync(
        Guid holidayId, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<HolidayDto>.Failure("Tenant context is not resolved.", 400);

        var holiday = await _dbContext.Holidays
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == holidayId, cancellationToken);
        if (holiday is null)
            return Result<HolidayDto>.Failure("Holiday not found.", 404);

        return Result<HolidayDto>.Success(await ToDtoWithLocationAsync(holiday, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<HolidayDto>>> GetAllAsync(
        DateOnly? from,
        DateOnly? to,
        int? year,
        Guid? locationId,
        bool? activeOnly,
        CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<IReadOnlyList<HolidayDto>>.Failure("Tenant context is not resolved.", 400);

        var query = _dbContext.Holidays.AsNoTracking();

        // FR-6: explicit date range takes precedence over a year-based list (AC-4).
        if (from.HasValue && to.HasValue)
        {
            query = query.Where(h => h.Date >= from.Value && h.Date <= to.Value);
        }
        else if (from.HasValue)
        {
            query = query.Where(h => h.Date >= from.Value);
        }
        else if (to.HasValue)
        {
            query = query.Where(h => h.Date <= to.Value);
        }
        else if (year.HasValue)
        {
            var start = new DateOnly(year.Value, 1, 1);
            var end = new DateOnly(year.Value, 12, 31);
            query = query.Where(h => h.Date >= start && h.Date <= end);
        }

        if (locationId.HasValue)
            query = query.Where(h => h.LocationId == locationId.Value);

        if (activeOnly == true)
            query = query.Where(h => h.IsActive);

        var holidays = await query
            .OrderBy(h => h.Date)
            .ThenBy(h => h.Name)
            .ToListAsync(cancellationToken);

        var dtos = await ToDtosWithLocationsAsync(holidays, cancellationToken);
        return Result<IReadOnlyList<HolidayDto>>.Success(dtos);
    }

    // ══════════════════════════════════════════════════════════════
    //  CSV import (FR-4, AC-3, NFR-3)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<HolidayImportResult>> ImportCsvAsync(
        Stream csvStream, string fileName, CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.IsResolved)
            return Result<HolidayImportResult>.Failure("Tenant context is not resolved.", 400);

        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        if (ext is not null and not ".csv")
            return Result<HolidayImportResult>.Failure(
                "Unsupported file format. Only .csv files are accepted.", 400);

        List<CsvHolidayRow> rows;
        try
        {
            rows = ParseCsvRows(csvStream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse holiday import file {FileName}", fileName);
            return Result<HolidayImportResult>.Failure($"Failed to parse file: {ex.Message}", 400);
        }

        if (rows.Count == 0)
            return Result<HolidayImportResult>.Failure("The import file contains no data rows.", 400);

        if (rows.Count > MaxImportRows)
            return Result<HolidayImportResult>.Failure(
                $"The import file contains {rows.Count} rows, exceeding the maximum of {MaxImportRows}.", 400);

        // Pre-load existing tenant-wide dates so DB duplicates are flagged too (location-specific
        // CSV import is not supported; all imported rows are tenant-wide, LocationId == null).
        var existingDates = await _dbContext.Holidays
            .Where(h => h.LocationId == null)
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);
        var existingDateSet = new HashSet<DateOnly>(existingDates);

        var errors = new List<HolidayImportRowError>();
        var toCreate = new List<Holiday>();
        var seenInFile = new HashSet<DateOnly>();

        foreach (var row in rows)
        {
            var rowErrors = new List<HolidayImportRowError>();

            if (string.IsNullOrWhiteSpace(row.Name))
                rowErrors.Add(Err(row.RowNumber, "name", "Holiday name is required."));
            else if (row.Name.Trim().Length > 100)
                rowErrors.Add(Err(row.RowNumber, "name", "Holiday name must be at most 100 characters."));

            DateOnly date = default;
            if (string.IsNullOrWhiteSpace(row.Date))
            {
                rowErrors.Add(Err(row.RowNumber, "date", "Date is required."));
            }
            else if (!DateOnly.TryParse(row.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                rowErrors.Add(Err(row.RowNumber, "date", "Invalid date format. Use YYYY-MM-DD."));
            }

            var type = HolidayType.Public;
            if (!string.IsNullOrWhiteSpace(row.Type))
            {
                if (!Enum.TryParse(row.Type.Trim(), true, out type))
                    rowErrors.Add(Err(row.RowNumber, "type", "Invalid type. Allowed: Public, Restricted, Optional."));
            }

            // BR-1 / AC-3: flag duplicates (same date) rather than silently skipping.
            if (rowErrors.Count == 0)
            {
                if (existingDateSet.Contains(date))
                    rowErrors.Add(Err(row.RowNumber, "date", $"A holiday already exists on {date:yyyy-MM-dd}."));
                else if (!seenInFile.Add(date))
                    rowErrors.Add(Err(row.RowNumber, "date", $"Duplicate date {date:yyyy-MM-dd} within the file."));
            }

            if (rowErrors.Count > 0)
            {
                errors.AddRange(rowErrors);
                continue;
            }

            toCreate.Add(new Holiday
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = _tenantContext.TenantId,
                Name = row.Name!.Trim(),
                Date = date,
                Type = type,
                LocationId = null,
                IsRecurring = false,
                IsActive = true,
                IsDeleted = false,
            });
        }

        if (toCreate.Count > 0)
        {
            _dbContext.Holidays.AddRange(toCreate);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        int failedRows = errors.Select(e => e.RowNumber).Distinct().Count();

        _logger.LogInformation(
            "Holiday CSV import completed. File={FileName}, Total={Total}, Created={Created}, Failed={Failed}, TenantId={TenantId}",
            fileName, rows.Count, toCreate.Count, failedRows, _tenantContext.TenantId);

        return Result<HolidayImportResult>.Success(new HolidayImportResult
        {
            Total = rows.Count,
            Created = toCreate.Count,
            Failed = failedRows,
            Errors = errors,
        });
    }

    // ══════════════════════════════════════════════════════════════
    //  Recurring generation (FR-3, BR-5)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<int>> GenerateRecurringForYearAsync(
        Guid tenantId, int targetYear, CancellationToken cancellationToken = default)
    {
        // Caller (the Hangfire job) sets the tenant context for this scope, so the global
        // query filters apply. We use the (set) tenant context for the new rows' TenantId.

        // Source: active recurring holidays from the year prior to the target year.
        int sourceYear = targetYear - 1;
        var sourceStart = new DateOnly(sourceYear, 1, 1);
        var sourceEnd = new DateOnly(sourceYear, 12, 31);

        var recurring = await _dbContext.Holidays
            .Where(h => h.IsRecurring && h.IsActive
                        && h.Date >= sourceStart && h.Date <= sourceEnd)
            .ToListAsync(cancellationToken);

        if (recurring.Count == 0)
            return Result<int>.Success(0);

        var targetStart = new DateOnly(targetYear, 1, 1);
        var targetEnd = new DateOnly(targetYear, 12, 31);

        // Existing target-year rows, keyed by (date, locationId) for idempotency.
        var existing = await _dbContext.Holidays
            .Where(h => h.Date >= targetStart && h.Date <= targetEnd)
            .Select(h => new { h.Date, h.LocationId })
            .ToListAsync(cancellationToken);
        var existingKeys = existing
            .Select(e => (e.Date, e.LocationId))
            .ToHashSet();

        var toCreate = new List<Holiday>();
        foreach (var source in recurring)
        {
            var nextDate = AddYearSafe(source.Date, 1);
            var key = (nextDate, source.LocationId);
            if (existingKeys.Contains(key))
                continue; // idempotent: next year's entry already exists.

            existingKeys.Add(key);
            toCreate.Add(new Holiday
            {
                Id = BaseEntity.NewUuidV7(),
                TenantId = tenantId,
                Name = source.Name,
                Date = nextDate,
                Type = source.Type,
                LocationId = source.LocationId,
                Description = source.Description,
                IsRecurring = true,
                IsActive = true,
                IsDeleted = false,
            });
        }

        if (toCreate.Count > 0)
        {
            _dbContext.Holidays.AddRange(toCreate);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Holiday recurrence generated {Count} entries for year {Year}, tenant {TenantId}.",
            toCreate.Count, targetYear, tenantId);

        return Result<int>.Success(toCreate.Count);
    }

    // ══════════════════════════════════════════════════════════════
    //  Tenant seeding (FR-5) — UNWIRED, see TODO(onboarding)
    // ══════════════════════════════════════════════════════════════

    public async Task<Result<int>> SeedHolidaysForTenantAsync(
        Guid tenantId, string countryCode, CancellationToken cancellationToken = default)
    {
        // Idempotent: skip if any holidays already exist for the tenant.
        var hasExisting = await _dbContext.Holidays
            .IgnoreQueryFilters()
            .AnyAsync(h => h.TenantId == tenantId && !h.IsDeleted, cancellationToken);
        if (hasExisting)
        {
            _logger.LogInformation(
                "Skipping holiday seeding — tenant {TenantId} already has holidays.", tenantId);
            return Result<int>.Success(0);
        }

        var template = LoadCountryTemplate(countryCode);
        if (template is null)
            return Result<int>.Failure($"No holiday template found for country '{countryCode}'.", 404);

        int year = DateTime.UtcNow.Year;
        var holidays = template.Holidays.Select(t => new Holiday
        {
            Id = BaseEntity.NewUuidV7(),
            TenantId = tenantId,
            Name = t.Name,
            Date = new DateOnly(year, t.Month, t.Day),
            Type = Enum.TryParse<HolidayType>(t.Type, true, out var ht) ? ht : HolidayType.Public,
            LocationId = null,
            IsRecurring = t.IsRecurring,
            IsActive = true,
            IsDeleted = false,
            CreatedBy = "system-seed",
        }).ToList();

        _dbContext.Holidays.AddRange(holidays);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Seeded {Count} holidays for tenant {TenantId} from template {Country}.",
            holidays.Count, tenantId, countryCode);

        return Result<int>.Success(holidays.Count);
    }

    // ══════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════

    private Task<bool> DuplicateExistsAsync(
        DateOnly date, Guid? locationId, Guid? excludeId, CancellationToken cancellationToken)
    {
        // BR-1: unique date per tenant + location. Tenant scope is enforced by the global filter.
        return _dbContext.Holidays.AnyAsync(h =>
            h.Date == date &&
            h.LocationId == locationId &&
            (excludeId == null || h.Id != excludeId.Value),
            cancellationToken);
    }

    private static string BuildDuplicateMessage(DateOnly date, Guid? locationId)
        => locationId.HasValue
            ? $"A holiday already exists on {date:yyyy-MM-dd} for this location."
            : $"A holiday already exists on {date:yyyy-MM-dd}.";

    /// <summary>
    /// Adds whole years to a date, clamping Feb 29 to Feb 28 on non-leap years (BR-5 edge).
    /// </summary>
    internal static DateOnly AddYearSafe(DateOnly date, int years)
    {
        int targetYear = date.Year + years;
        int day = date.Day;
        if (date.Month == 2 && date.Day == 29 && !DateTime.IsLeapYear(targetYear))
            day = 28;
        return new DateOnly(targetYear, date.Month, day);
    }

    private async Task<HolidayDto> ToDtoWithLocationAsync(Holiday h, CancellationToken cancellationToken)
    {
        string? locationName = null;
        if (h.LocationId.HasValue)
        {
            locationName = await _dbContext.Locations
                .Where(l => l.Id == h.LocationId.Value)
                .Select(l => l.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }
        return ToDto(h, locationName);
    }

    private async Task<IReadOnlyList<HolidayDto>> ToDtosWithLocationsAsync(
        IReadOnlyList<Holiday> holidays, CancellationToken cancellationToken)
    {
        var locationIds = holidays.Where(h => h.LocationId.HasValue)
            .Select(h => h.LocationId!.Value).Distinct().ToList();

        var nameById = locationIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _dbContext.Locations
                .Where(l => locationIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => l.Name, cancellationToken);

        return holidays.Select(h => ToDto(
            h, h.LocationId.HasValue && nameById.TryGetValue(h.LocationId.Value, out var n) ? n : null))
            .ToList();
    }

    private static HolidayDto ToDto(Holiday h, string? locationName) => new()
    {
        Id = h.Id,
        Name = h.Name,
        Date = h.Date,
        Type = h.Type.ToString(),
        LocationId = h.LocationId,
        LocationName = locationName,
        Description = h.Description,
        IsRecurring = h.IsRecurring,
        IsActive = h.IsActive,
        CreatedAt = h.CreatedAt,
        UpdatedAt = h.UpdatedAt,
    };

    private static HolidayImportRowError Err(int rowNumber, string field, string error)
        => new() { RowNumber = rowNumber, Field = field, Error = error };

    // ── CSV parsing (reuses CsvHelper, the library already used by bulk employee import) ──

    internal static List<CsvHolidayRow> ParseCsvRows(Stream stream)
    {
        var rows = new List<CsvHolidayRow>();

        if (stream.CanSeek)
            stream.Position = 0;

        using var reader = new StreamReader(
            stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);

        // Skip leading comment lines (starting with #).
        string? headerLine;
        while ((headerLine = reader.ReadLine()) != null)
        {
            if (!headerLine.TrimStart().StartsWith('#'))
                break;
        }

        if (string.IsNullOrWhiteSpace(headerLine))
            return rows;

        var fullCsv = headerLine + "\n" + reader.ReadToEnd();
        using var stringReader = new StringReader(fullCsv);
        using var csv = new CsvReader(stringReader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim,
        });

        csv.Read();
        csv.ReadHeader();

        int rowNum = 1;
        while (csv.Read())
        {
            rowNum++;
            rows.Add(new CsvHolidayRow
            {
                RowNumber = rowNum,
                Name = GetField(csv, "name"),
                Date = GetField(csv, "date"),
                Type = GetField(csv, "type"),
            });
        }

        return rows;
    }

    private static string? GetField(CsvReader reader, string name)
    {
        try
        {
            var value = reader.GetField(name);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static CountryHolidayTemplate? LoadCountryTemplate(string countryCode)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"HolidayTemplates.holidays.{countryCode.ToUpperInvariant()}.json",
                StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            return null;

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        return JsonSerializer.Deserialize<CountryHolidayTemplate>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
    }

    internal sealed class CsvHolidayRow
    {
        public int RowNumber { get; init; }
        public string? Name { get; init; }
        public string? Date { get; init; }
        public string? Type { get; init; }
    }

    internal sealed class CountryHolidayTemplate
    {
        public string CountryCode { get; init; } = string.Empty;
        public List<TemplateHoliday> Holidays { get; init; } = [];
    }

    internal sealed class TemplateHoliday
    {
        public string Name { get; init; } = string.Empty;
        public int Month { get; init; }
        public int Day { get; init; }
        public string Type { get; init; } = "Public";
        public bool IsRecurring { get; init; }
    }
}
