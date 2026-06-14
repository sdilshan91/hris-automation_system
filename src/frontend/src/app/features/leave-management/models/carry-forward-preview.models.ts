/**
 * US-LV-008: Leave Carry-Forward & Expiry — preview report models.
 *
 * Backend endpoint (backend agent building in parallel — assumed contract,
 * RECONCILE against docs/vault/modules/leave-management.md "Frontend (US-LV-008)"):
 *
 *   GET /api/v1/leaves/carry-forward-preview?year={year}
 *       -> ICarryForwardPreviewRow[]   (FR-5, AC-5)
 *
 * The report is READ-ONLY (§10): it does not lock or commit any data. The
 * authoritative carry-forward / forfeiture is produced by the two backend
 * Hangfire jobs (ProcessLeaveYearEndJob, ProcessCarryForwardExpiryJob); this
 * preview just shows what those jobs *would* produce for the closing year.
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource base is
 * `${apiBaseUrl}/leaves`.
 */

/**
 * One projected carry-forward / forfeiture row per (employee × leave type) for
 * the selected closing year (FR-5).
 *
 * `departmentName` is OPTIONAL in the contract (denormalized for the department
 * filter + display). If the backend cannot denormalize it, the department filter
 * simply won't have that row's department as an option — the rest still renders.
 */
export interface ICarryForwardPreviewRow {
  employeeId: string;
  employeeName: string;
  departmentName?: string | null;
  leaveTypeId: string;
  leaveTypeName: string;
  /** Days that would carry into the new year — MIN(unusedBalance, limit) (BR-1). */
  projectedCarryForward: number;
  /** Days that would be forfeited — unusedBalance - carryForward, if > 0 (BR-2). */
  projectedForfeiture: number;
}

/** Error envelope surfaced via toast (matches the module-wide error contract). */
export interface ICarryForwardPreviewError {
  message: string;
  code?: string;
}

/**
 * The set of closing years offered in the year-selector at the top of the report.
 * Defaults to a small window ending at the current year so HR can preview the
 * upcoming year-end and review the prior one. Returned newest-first for the
 * dropdown.
 */
export function buildPreviewYearOptions(currentYear: number, lookback = 2, lookahead = 1): number[] {
  const years: number[] = [];
  for (let y = currentYear + lookahead; y >= currentYear - lookback; y--) {
    years.push(y);
  }
  return years;
}

/**
 * Pure helper: case-insensitive substring match used by the employee text filter.
 * Empty/whitespace term matches everything.
 */
export function matchesEmployeeTerm(row: ICarryForwardPreviewRow, term: string): boolean {
  const t = term.trim().toLowerCase();
  if (!t) {
    return true;
  }
  return row.employeeName.toLowerCase().includes(t);
}

/**
 * Pure helper: distinct, sorted department names present in the rows (for the
 * department filter dropdown). Null/empty departments are excluded.
 */
export function distinctDepartments(rows: ICarryForwardPreviewRow[]): string[] {
  const set = new Set<string>();
  for (const r of rows) {
    const d = (r.departmentName ?? '').trim();
    if (d) {
      set.add(d);
    }
  }
  return Array.from(set).sort((a, b) => a.localeCompare(b));
}

/**
 * Pure helper: distinct leave types present in the rows (for the leave-type
 * filter dropdown), keyed by id with the denormalized name.
 */
export function distinctLeaveTypes(rows: ICarryForwardPreviewRow[]): { id: string; name: string }[] {
  const map = new Map<string, string>();
  for (const r of rows) {
    if (!map.has(r.leaveTypeId)) {
      map.set(r.leaveTypeId, r.leaveTypeName);
    }
  }
  return Array.from(map.entries())
    .map(([id, name]) => ({ id, name }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

/** Totals shown in the report summary strip. */
export interface IPreviewTotals {
  carryForward: number;
  forfeiture: number;
  rows: number;
}

/** Pure helper: sum carry-forward + forfeiture across the given rows. */
export function sumTotals(rows: ICarryForwardPreviewRow[]): IPreviewTotals {
  let carryForward = 0;
  let forfeiture = 0;
  for (const r of rows) {
    carryForward += r.projectedCarryForward;
    forfeiture += r.projectedForfeiture;
  }
  return { carryForward, forfeiture, rows: rows.length };
}
