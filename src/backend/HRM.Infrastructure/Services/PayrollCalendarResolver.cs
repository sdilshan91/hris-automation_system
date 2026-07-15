using HRM.Application.Common.Interfaces;
using HRM.Domain.Entities;
using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// US-ATT-011 AC-4 / FR-5 (CAL-5): the ONE place the payroll engines resolve (a) the effective
/// <see cref="TenantPayrollCalendarPolicy"/> and (b) the LOCATION-scoped public-holiday set for a period.
///
/// <para><b>Why this is shared rather than inlined:</b> holiday exclusion is only correct if it is applied on
/// the SAME basis at every site that counts working days for payroll — the pro-ration DENOMINATOR
/// (<c>AttendancePayrollService.TotalWorkingDays</c> / <c>PayrollRunProcessor</c>'s shift-days fallback) AND
/// the pro-ration NUMERATOR (<c>PayrollRunProcessor.ProRataPaidDays</c>). Excluding holidays from the
/// denominator alone RAISES a mid-month joiner's pro-ration factor and OVER-PAYS them (masked by the
/// <c>paidDaysBeforeLop > workingDays</c> clamp). Routing every site through this one resolver is what stops
/// the two sides drifting apart. The rule it encodes is simply: <b>a public holiday is not a working
/// day.</b></para>
///
/// <para><b>Batching (no N+1):</b> the payroll run loops every employee in the tenant, so the holiday set is
/// resolved ONCE per DISTINCT <c>Employee.LocationId</c> for the period — not per employee. Query cost is flat
/// in employee count: 1 policy query + at most (distinct locations + 1) holiday queries per run.</para>
///
/// <para><b>Default OFF ⇒ zero behaviour change:</b> when no policy row exists (every existing tenant), or the
/// resolved version has the flag false, <see cref="HolidaysByLocationAsync"/> is never called and callers thread
/// a null holiday set — so every figure is byte-identical to the pre-CAL-5 engine.</para>
/// </summary>
internal static class PayrollCalendarResolver
{
    /// <summary>
    /// The effective policy version at <paramref name="asOf"/> (latest <c>EffectiveFrom</c> ≤ asOf among active
    /// versions), or null when the tenant has configured none. Mirrors <c>FnFPolicyService.GetEffectiveAsync</c>
    /// — the same resolution semantics, so a policy change applies to the NEXT period, never retroactively.
    /// Tenant-scoped by the EF global query filter.
    /// </summary>
    public static Task<TenantPayrollCalendarPolicy?> ResolvePolicyAsync(
        AppDbContext db, DateOnly asOf, CancellationToken ct)
        => db.TenantPayrollCalendarPolicies.AsNoTracking()
            .Where(p => p.IsActive && p.EffectiveFrom <= asOf)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Whether public holidays must be excluded from the payroll working-days count for the period starting at
    /// <paramref name="asOf"/>. <b>No policy row → false</b> (the code-default; see
    /// <see cref="TenantPayrollCalendarPolicy.ExcludeHolidaysFromWorkingDays"/> for why the default is not true).
    /// </summary>
    public static async Task<bool> ExcludeHolidaysAsync(AppDbContext db, DateOnly asOf, CancellationToken ct)
        => (await ResolvePolicyAsync(db, asOf, ct))?.ExcludeHolidaysFromWorkingDays ?? false;

    /// <summary>
    /// Builds the location → public-holiday-date-set map for the inclusive period, querying ONCE per DISTINCT
    /// location (including the "no location" bucket) rather than once per employee. Reuses
    /// <see cref="IHolidayProvider"/> — the same location-aware provider the overtime path routes onto (CAL-3 /
    /// BUG-286) — so location semantics (tenant-wide holidays always apply; a location's own holidays apply only
    /// to that location) are defined in exactly one place.
    /// <para>Keyed by <see cref="NoLocation"/> for employees with no location, because a nullable key cannot
    /// satisfy Dictionary's notnull constraint. Real location ids are UUIDv7, never <see cref="Guid.Empty"/>.</para>
    /// </summary>
    public static async Task<Dictionary<Guid, IReadOnlySet<DateOnly>>> HolidaysByLocationAsync(
        IHolidayProvider holidays, IEnumerable<Guid?> locationIds, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var map = new Dictionary<Guid, IReadOnlySet<DateOnly>>();
        foreach (var locationId in locationIds.Distinct())
            map[Key(locationId)] = await holidays.GetHolidaysAsync(start, end, locationId, ct);
        return map;
    }

    /// <summary>
    /// This employee's holiday set from a map built by <see cref="HolidaysByLocationAsync"/>, or <c>null</c> when
    /// <paramref name="map"/> is null (the flag is off) — null is the caller's "do not exclude anything" signal,
    /// which keeps the flag-off path identical to the pre-CAL-5 engine.
    /// </summary>
    public static IReadOnlySet<DateOnly>? For(Dictionary<Guid, IReadOnlySet<DateOnly>>? map, Guid? locationId)
        => map is null ? null : map.GetValueOrDefault(Key(locationId));

    /// <summary>The map key for the "employee has no location" bucket (tenant-wide holidays only).</summary>
    private static readonly Guid NoLocation = Guid.Empty;

    private static Guid Key(Guid? locationId) => locationId ?? NoLocation;
}
