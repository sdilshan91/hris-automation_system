namespace HRM.Application.Common.Interfaces;

/// <summary>
/// ISSUE-305: resolves the current tenant's leave-year model — the one place that reads
/// <c>Tenant.FiscalYearStartMonth</c> <b>from inside a tenant scope</b>.
///
/// <para><b>The two deliberate exceptions</b> (don't "fix" them): <c>LeaveAccrualJob</c> and
/// <c>ProcessLeaveYearEndJob</c> read the column directly in their cross-tenant sweep, because they run
/// OUTSIDE any tenant scope and must know the month before they can decide which scope to create. They
/// project it in the same query that lists tenants. Every other reader goes through this interface.</para>
///
/// <para><b>Why this exists.</b> ISSUE-305 was not an arithmetic bug; it was a WIRING bug. The column existed,
/// was settable in the org-profile UI, and was read by nothing, so the leave subsystem hardcoded calendar
/// Jan–Dec in a dozen places. Fixing it site-by-site reproduced the same tenant lookup in every service, and a
/// site that was missed silently disagreed with the sites that weren't — which is exactly how the ledger came
/// to have a fiscal CREDIT side and a calendar DEBIT side. One injected resolver makes "did this site read the
/// column?" answerable by looking at the constructor.</para>
///
/// <para><b>The label contract.</b> A leave year is identified by an <c>int</c> (<c>LeaveLedger.LeaveYear</c>)
/// and is labelled by the calendar year it STARTS in. For a January tenant that is just the calendar year, so
/// <see cref="LabelForAsync(DateOnly, CancellationToken)"/> returns exactly what <c>date.Year</c> used to —
/// every calendar tenant is byte-identical to the pre-ISSUE-305 behaviour. For an Apr–Mar tenant, 2027-02-10
/// belongs to leave year <b>2026</b>, which is the value <c>date.Year</c> gets wrong for three months a year.
/// </para>
///
/// <para><b>Every ledger read and write must agree.</b> The ledger is keyed by this label, so a site that
/// derives it any other way (<c>date.Year</c>, <c>DateTime.UtcNow.Year</c>, a payroll year) will read a
/// different bucket than the accrual wrote and see a zero balance.</para>
/// </summary>
public interface ITenantLeaveYearResolver
{
    /// <summary>
    /// The tenant's leave-year start month (1–12; 1 = calendar). Falls back to calendar when no tenant row
    /// resolves, so a background sweep degrades to the old behaviour rather than throwing.
    /// </summary>
    Task<int> GetStartMonthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The leave year <paramref name="date"/> falls in for the current tenant — the <c>LeaveLedger.LeaveYear</c>
    /// label to read/write. Replaces every raw <c>date.Year</c> on a ledger path.
    /// </summary>
    Task<int> LabelForAsync(DateOnly date, CancellationToken cancellationToken = default);
}
