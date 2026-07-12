using HRM.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HRM.Infrastructure.Services;

/// <summary>
/// BUG-125: shared, batched shift-schedule resolution for the attendance dashboard + payroll paths.
///
/// Resolves each employee's working-weekday set (ISO 1=Mon..7=Sun) as of a single "as-of" date using a
/// FIXED number of queries (three: default shift, effective assignments, referenced shifts) regardless of
/// employee count. This replaces the former per-employee 2-3-query N+1 that
/// <c>AttendancePayrollService.WorkingDaysAsync</c> and <c>AttendanceDashboardService.ResolveWorkingDaysAsync</c>
/// ran inside a per-employee loop (≈15k round-trips at 5k employees).
///
/// Shift selection is byte-for-byte identical to those per-employee originals: the latest EmployeeShift
/// assignment effective at <paramref name="asOf"/> (<c>EffectiveFrom &lt;= asOf</c> and <c>EffectiveTo</c>
/// null or <c>&gt;= asOf</c>, highest <c>EffectiveFrom</c> wins), falling back to the tenant default shift,
/// then to an empty set — which the callers treat as "every calendar day is a working day". All queries
/// run under the EF global tenant query filter.
/// </summary>
internal static class ShiftScheduleResolver
{
    /// <summary>
    /// Resolves the working-weekday set for every employee in <paramref name="employeeIds"/> as of
    /// <paramref name="asOf"/>. The returned dictionary contains an entry for every requested id (an empty
    /// set meaning "no shift resolved → all calendar days are working days"). The sets are read-only to
    /// callers; several employees may share the same (default/assigned) set instance.
    /// </summary>
    public static async Task<Dictionary<Guid, HashSet<int>>> ResolveWorkingDaySetsAsync(
        AppDbContext db, IReadOnlyList<Guid> employeeIds, DateOnly asOf, CancellationToken ct)
    {
        var result = new Dictionary<Guid, HashSet<int>>(employeeIds.Count);
        if (employeeIds.Count == 0) return result;

        // (1) tenant default shift — ONE query (fallback when no assignment / assigned shift missing).
        var defaultShift = await db.Shifts.AsNoTracking()
            .FirstOrDefaultAsync(s => s.IsDefault, ct);
        var defaultDays = defaultShift?.WorkingDays.ToHashSet() ?? new HashSet<int>();

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

        // (3) the referenced shifts' working-days — ONE query.
        var shiftIds = shiftIdByEmp.Values.Distinct().ToList();
        var daysByShiftId = shiftIds.Count == 0
            ? new Dictionary<Guid, HashSet<int>>()
            : (await db.Shifts.AsNoTracking()
                    .Where(s => shiftIds.Contains(s.Id))
                    .Select(s => new { s.Id, s.WorkingDays })
                    .ToListAsync(ct))
                .ToDictionary(s => s.Id, s => s.WorkingDays.ToHashSet());

        // (4) resolve each employee IN MEMORY: assigned shift days → default shift days → empty set.
        //     Matches the original `shift ??= default` fallback (assignment present but shift not found
        //     also falls back to the default).
        foreach (var id in employeeIds)
        {
            HashSet<int> days = defaultDays;
            if (shiftIdByEmp.TryGetValue(id, out var shiftId)
                && daysByShiftId.TryGetValue(shiftId, out var assignedDays))
            {
                days = assignedDays;
            }
            result[id] = days;
        }

        return result;
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
