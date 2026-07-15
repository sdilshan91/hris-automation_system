using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// BUG-125 / US-ATT-011: shared, batched shift-schedule resolution for the attendance dashboard, leave and
/// payroll paths — the single source of truth for "is date D a working day for employee E".
///
/// Resolves each employee's working-weekday set (ISO 1=Mon..7=Sun) as of a single "as-of" date using a
/// FIXED number of queries (five at most: default shift, effective assignments, employees' locations, those
/// locations' default shifts, and the referenced shifts) regardless of employee count. This replaces the
/// former per-employee 2-3-query N+1 that <c>AttendancePayrollService.WorkingDaysAsync</c> and
/// <c>AttendanceDashboardService.ResolveWorkingDaysAsync</c> ran inside a per-employee loop (≈15k
/// round-trips at 5k employees).
///
/// <para><b>Four-tier resolution chain (US-ATT-011 AC-2, BR-3) — first tier that resolves wins:</b></para>
/// <list type="number">
///   <item><description><b>Employee</b> — the latest effective-dated <c>EmployeeShift</c> assignment at
///     <c>asOf</c> (<c>EffectiveFrom &lt;= asOf</c> and <c>EffectiveTo</c> null or <c>&gt;= asOf</c>, highest
///     <c>EffectiveFrom</c> wins; picked in memory).</description></item>
///   <item><description><b>Location</b> — the employee's <c>Employee.LocationId</c> →
///     <c>Location.DefaultShiftId</c> → that shift's <c>WorkingDays</c>. This is what lets a Gulf branch run
///     Sun–Thu (<c>{7,1,2,3,4}</c>) while the tenant default stays Mon–Fri, with no per-employee
///     assignment.</description></item>
///   <item><description><b>Tenant</b> — the tenant default shift (<c>Shift.IsDefault</c>).</description></item>
///   <item><description><b>Code default</b> — <see cref="CodeDefaultWorkingDays"/> = Mon–Fri
///     <c>{1,2,3,4,5}</c>.</description></item>
/// </list>
///
/// <para><b>Behaviour change (US-ATT-011) — this is NOT a no-op:</b> this method previously fell through to an
/// EMPTY set, which its callers interpret as "every calendar day is a working day" — so an unconfigured tenant
/// (or one that had deleted its default shift, BUG-287) silently counted weekends as working days, inflating
/// payroll pro-rata denominators from ~22 to ~30. The final fallback is now the explicit Mon–Fri code default.
/// For such a tenant, pro-rata denominators change on the next run — deliberately, and in the correct
/// direction.</para>
///
/// <para><b>This method never returns an empty set</b>, and that guarantee needs BOTH halves: the code-default
/// fallback (no shift row) AND <see cref="ToCalendar"/> (a shift row that declares NO working days — legal for
/// a Flexible shift, since the validator only requires WorkingDays for Single/Rotating). A tier whose shift
/// declares no days does not resolve; it falls through. Without that, a Flexible shift wired to a Location
/// would SHADOW tiers 3-4 with an empty set and hand that branch a 7-day working week — reintroducing the very
/// bug this chain exists to remove. The "empty = every calendar day" rule still stands in
/// <see cref="CountWorkingDays"/> for any caller that passes a set from elsewhere.</para>
///
/// <para>All queries run under the EF global tenant query filter, which is what makes a cross-tenant
/// <c>Location.DefaultShiftId</c> silently fail to resolve rather than leak (TC-ATT-ISO-014).</para>
/// </summary>
internal static class ShiftScheduleResolver
{
    /// <summary>
    /// US-ATT-011 BR-3 tier 4: the code default working week, Mon–Fri (ISO 1=Mon..7=Sun). Used when an
    /// employee has no personal assignment, no location default, and the tenant has no default shift.
    /// </summary>
    public static readonly IReadOnlySet<int> CodeDefaultWorkingDays = new HashSet<int> { 1, 2, 3, 4, 5 };

    /// <summary>
    /// Resolves the working-weekday set for every employee in <paramref name="employeeIds"/> as of
    /// <paramref name="asOf"/> using the four-tier chain (Employee → Location → Tenant → code default).
    /// The returned dictionary contains an entry for every requested id and is never empty for any id —
    /// an unconfigured employee resolves to the Mon–Fri code default. The sets are read-only to callers;
    /// several employees may share the same (default/location/assigned) set instance.
    /// </summary>
    public static async Task<Dictionary<Guid, HashSet<int>>> ResolveWorkingDaySetsAsync(
        AppDbContext db, IReadOnlyList<Guid> employeeIds, DateOnly asOf, CancellationToken ct)
    {
        var result = new Dictionary<Guid, HashSet<int>>(employeeIds.Count);
        if (employeeIds.Count == 0) return result;

        // (1) tenant default shift — ONE query (tier 3; used when neither an assignment nor a location
        //     default resolves). No default shift — OR a default shift carrying NO working days — → tier 4,
        //     the Mon-Fri code default. The Count > 0 guard is load-bearing: a Flexible shift is not required
        //     to declare WorkingDays (ShiftRequestValidator only enforces NotEmpty for Single/Rotating), so
        //     `WorkingDays` can legitimately be empty — and an empty set is NOT a calendar, it is the
        //     "every calendar day is a working day" sentinel that CountWorkingDays applies. Without this
        //     guard a Flexible tenant default would silently mean a 7-day working week.
        var defaultShift = await db.Shifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsDefault, ct);
        var defaultDays = ToCalendar(defaultShift?.WorkingDays) ?? CodeDefaultWorkingDays.ToHashSet();

        // (2) all candidate assignments effective at asOf — ONE query. The latest EffectiveFrom per
        //     employee is picked IN MEMORY (identical to the per-employee OrderByDescending().First()).
        var assignments = await db.EmployeeShifts.AsNoTracking()
            .Where(es => employeeIds.Contains(es.EmployeeId)
                && es.EffectiveFrom <= asOf
                && (es.EffectiveTo == null || es.EffectiveTo >= asOf))
            .Select(es => new { es.EmployeeId, es.ShiftId, es.EffectiveFrom })
            .ToListAsync(ct);

        var shiftIdByEmp = assignments
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.EffectiveFrom).First().ShiftId);

        // (3) US-ATT-011 tier 2 — the employees' locations — ONE query. NOTE: the FK is `LocationId` and the
        //     nav is `LocationEntity`; `Employee.Location` is a LEGACY free-text string, not this link.
        var locationIdByEmp = (await db.Employees.AsNoTracking()
                .Where(e => employeeIds.Contains(e.Id) && e.LocationId != null)
                .Select(e => new { e.Id, LocationId = e.LocationId!.Value })
                .ToListAsync(ct))
            .ToDictionary(e => e.Id, e => e.LocationId);

        // (4) those locations' default shifts — ONE query. Under the global tenant filter, so a location
        //     pointing at another tenant's shift simply resolves nothing (TC-ATT-ISO-014).
        var locationIds = locationIdByEmp.Values.Distinct().ToList();
        var shiftIdByLocation = locationIds.Count == 0
            ? new Dictionary<Guid, Guid>()
            : (await db.Locations.AsNoTracking()
                    .Where(l => locationIds.Contains(l.Id) && l.DefaultShiftId != null)
                    .Select(l => new { l.Id, DefaultShiftId = l.DefaultShiftId!.Value })
                    .ToListAsync(ct))
                .ToDictionary(l => l.Id, l => l.DefaultShiftId);

        // (5) the referenced shifts' working-days — ONE query for BOTH tiers: the assigned shifts and the
        //     location-default shifts are unioned into a single id set so adding the Location tier costs no
        //     extra round-trip (FR-3: query count is flat in employee count).
        var shiftIds = shiftIdByEmp.Values
            .Concat(shiftIdByLocation.Values)
            .Distinct()
            .ToList();
        var daysByShiftId = shiftIds.Count == 0
            ? new Dictionary<Guid, HashSet<int>>()
            : (await db.Shifts.AsNoTracking()
                    .Where(s => shiftIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.WorkingDays })
                    .ToListAsync(ct))
                .ToDictionary(s => s.Id, s => s.WorkingDays.ToHashSet());

        // (6) resolve each employee IN MEMORY down the four-tier chain — first tier that resolves wins:
        //     assigned shift → location default shift → tenant default shift → Mon-Fri code default
        //     (`defaultDays` already holds tier 3 or, absent a tenant default, tier 4).
        foreach (var id in employeeIds)
        {
            result[id] = ResolveDays(id) ?? defaultDays;
        }

        return result;

        // Tiers 1-2; null = neither resolved, so the caller falls through to `defaultDays`.
        // A tier whose shift declares NO working days does NOT resolve — it falls through rather than
        // returning an empty set (which CountWorkingDays would read as "every calendar day"). See ToCalendar.
        HashSet<int>? ResolveDays(Guid employeeId)
        {
            // Tier 1: personal effective-dated assignment.
            if (shiftIdByEmp.TryGetValue(employeeId, out var assignedShiftId)
                && daysByShiftId.TryGetValue(assignedShiftId, out var assignedDays)
                && assignedDays.Count > 0)
            {
                return assignedDays;
            }

            // Tier 2: the employee's location's default shift.
            if (locationIdByEmp.TryGetValue(employeeId, out var locationId)
                && shiftIdByLocation.TryGetValue(locationId, out var locationShiftId)
                && daysByShiftId.TryGetValue(locationShiftId, out var locationDays)
                && locationDays.Count > 0)
            {
                return locationDays;
            }

            return null;
        }
    }

    /// <summary>
    /// Normalizes a shift's declared working days into a calendar, or <c>null</c> when the shift declares
    /// none. An EMPTY working-day list is deliberately NOT a calendar: <see cref="CountWorkingDays"/> reads an
    /// empty set as "every calendar day is a working day", so returning one from a resolution tier would mean
    /// a 7-day working week instead of falling through to the next tier. A Flexible shift may legitimately
    /// carry no <c>WorkingDays</c> (the validator only requires them for Single/Rotating), so this is a real
    /// production state, not a defensive nicety.
    /// </summary>
    private static HashSet<int>? ToCalendar(IEnumerable<int>? workingDays)
    {
        if (workingDays is null) return null;
        var set = workingDays.ToHashSet();
        return set.Count > 0 ? set : null;
    }

    /// <summary>
    /// Counts working days in the inclusive range [<paramref name="start"/>, <paramref name="end"/>] for a
    /// resolved working-weekday set. An empty set counts every calendar day (the callers' "no shift = all
    /// days" rule). Returns 0 when the range is empty.
    /// </summary>
    public static int CountWorkingDays(HashSet<int> workingDays, DateOnly start, DateOnly end)
    {
        if (end < start) return 0;

        int count = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (workingDays.Count == 0 || workingDays.Contains(IsoDay(d)))
                count++;
        }
        return count;
    }

    private static int IsoDay(DateOnly date)
    {
        var dow = (int)date.DayOfWeek;   // Sun=0..Sat=6
        return dow == 0 ? 7 : dow;       // 1=Mon..7=Sun
    }
}
