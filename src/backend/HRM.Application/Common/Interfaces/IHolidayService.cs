using HRM.Application.Common.Models;
using HRM.Application.Features.Holidays.DTOs;

namespace HRM.Application.Common.Interfaces;

/// <summary>
/// Holiday calendar CRUD, range/list queries, CSV import, recurrence generation,
/// and tenant seeding (US-LV-007). All operations are tenant-scoped via ITenantContext
/// and EF global query filters.
/// </summary>
public interface IHolidayService
{
    Task<Result<HolidayDto>> CreateAsync(
        CreateHolidayRequest request, CancellationToken cancellationToken = default);

    Task<Result<HolidayDto>> UpdateAsync(
        Guid holidayId, UpdateHolidayRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deactivates a holiday (BR-4: cannot hard-delete within a finalized payroll
    /// period — soft-deactivate only since no payroll module exists yet).
    /// </summary>
    Task<Result> DeactivateAsync(Guid holidayId, CancellationToken cancellationToken = default);

    Task<Result<HolidayDto>> GetByIdAsync(Guid holidayId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists holidays for the current tenant. When <paramref name="from"/> and
    /// <paramref name="to"/> are supplied, returns holidays in that inclusive range
    /// (FR-6); otherwise when <paramref name="year"/> is supplied, returns that year's
    /// holidays (AC-4). Optionally filters by location and active state.
    /// </summary>
    Task<Result<IReadOnlyList<HolidayDto>>> GetAllAsync(
        DateOnly? from,
        DateOnly? to,
        int? year,
        Guid? locationId,
        bool? activeOnly,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk-creates holidays from a CSV stream (FR-4, AC-3, NFR-3). Columns: name, date, type.
    /// Per-row validation; duplicates (same date+location) are flagged in the result rather
    /// than silently skipped. Handles up to 100 rows.
    /// </summary>
    Task<Result<HolidayImportResult>> ImportCsvAsync(
        Stream csvStream, string fileName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates next-year entries for recurring holidays in the given tenant (FR-3, BR-5).
    /// Idempotent: skips a holiday if next year's entry already exists. Returns the number
    /// of holidays generated.
    /// </summary>
    Task<Result<int>> GenerateRecurringForYearAsync(
        Guid tenantId, int targetYear, CancellationToken cancellationToken = default);

    /// <summary>
    /// Seeds an initial holiday set for a newly provisioned tenant from a static country
    /// template (FR-5, §10). Idempotent: skips if any holidays already exist for the tenant.
    /// TODO(onboarding): wire into tenant provisioning / onboarding wizard Step 4.
    /// </summary>
    Task<Result<int>> SeedHolidaysForTenantAsync(
        Guid tenantId, string countryCode, CancellationToken cancellationToken = default);
}
