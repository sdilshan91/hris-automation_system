using HRM.Application.Common.Interfaces;
using HRM.Domain.Enums;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// DB-backed <see cref="IHolidayProvider"/> (US-LV-007 AC-2 / FR-6).
///
/// Returns active PUBLIC holiday dates in a range for the current tenant. Public holidays
/// are the only type excluded from leave day counts (BR-2/AC-2); restricted/optional holidays
/// are not auto-excluded. Tenant scope is enforced by the EF global query filter.
///
/// Location filtering (BR-2): when a location is supplied, tenant-wide holidays (LocationId
/// IS NULL) and holidays scoped to that location are returned; when null, only tenant-wide
/// holidays count — so a New-York-only holiday does not affect a London employee.
///
/// NFR-1 (Redis caching) is DEFERRED — we query the DB directly, consistent with the
/// module-wide deferred-Redis decision. The (tenant_id, date) index serves these range reads.
/// </summary>
public sealed class HolidayProvider : IHolidayProvider
{
    private readonly AppDbContext _dbContext;

    public HolidayProvider(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlySet<DateOnly>> GetHolidaysAsync(
        DateOnly startInclusive,
        DateOnly endInclusive,
        Guid? locationId = null,
        CancellationToken cancellationToken = default)
    {
        if (endInclusive < startInclusive)
            return new HashSet<DateOnly>();

        var query = _dbContext.Holidays
            .AsNoTracking()
            .Where(h => h.IsActive
                        && h.Type == HolidayType.Public
                        && h.Date >= startInclusive
                        && h.Date <= endInclusive);

        // Tenant-wide holidays always apply; location-specific ones only when they match.
        query = locationId.HasValue
            ? query.Where(h => h.LocationId == null || h.LocationId == locationId.Value)
            : query.Where(h => h.LocationId == null);

        var dates = await query
            .Select(h => h.Date)
            .ToListAsync(cancellationToken);

        return dates.ToHashSet();
    }
}
