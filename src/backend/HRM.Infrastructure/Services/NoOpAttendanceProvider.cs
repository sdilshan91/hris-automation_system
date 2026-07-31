using HRM.Application.Common.Interfaces;

namespace HRM.Infrastructure.Services;

/// <summary>
/// Default <see cref="IAttendanceProvider"/> that returns no absences (US-LV-011 FR-2 seam).
///
/// There is no attendance module yet (US-ATT-* unbuilt), so the auto-LOP absenteeism job runs and is
/// idempotent/tenant-safe but generates nothing. When attendance lands, register a DB-backed provider
/// in DI (mirroring how US-LV-007 swapped NoOpHolidayProvider for the real HolidayProvider).
///
/// <para><b>⚠ DO NOT "just register a real provider" — that would DOUBLE-DEDUCT PAY (ISSUE-357).</b> The
/// rationale above ("there is no attendance module yet") EXPIRED: attendance shipped under US-ATT-001/002/008
/// and absence data is available on <c>AttendanceMonthlySummary</c> (<c>TotalAbsentDays</c>, <c>LopDays</c>).
/// But payroll does NOT read the leave module's LOP — <c>PayrollRunProcessor</c> takes
/// <c>attendance?.LopDays</c> directly, so absence-driven deduction is ALREADY happening through the
/// attendance rail. Registering a DB-backed provider here would mint leave-side LOP rows for the same absent
/// days payroll already deducts: a second, parallel ledger.</para>
///
/// <para>Before any code: decide which LOP rail is authoritative and reconcile the two. That is a business
/// decision about how absence flows into pay, not a wiring job.</para>
/// </summary>
public sealed class NoOpAttendanceProvider : IAttendanceProvider
{
    private static readonly IReadOnlySet<DateOnly> Empty = new HashSet<DateOnly>();

    public Task<IReadOnlySet<DateOnly>> GetAbsentWorkingDaysAsync(
        Guid employeeId,
        DateOnly startInclusive,
        DateOnly endInclusive,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Empty);
}
