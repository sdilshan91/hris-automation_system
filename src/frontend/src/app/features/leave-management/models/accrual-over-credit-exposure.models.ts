/**
 * BUG-291: Accrual over-credit exposure — READ-ONLY remediation tooling.
 *
 * A leave-accrual defect credited a FULL YEAR on the first accrual run for some
 * Monthly/Quarterly leave types. The fix is forward-only, so affected employees
 * still hold over-credited balances that feed encashment and final settlement
 * (real money). Balances are NOT auto-corrected — HR/Finance work this list
 * case-by-case. This screen surfaces the affected population so a human can act
 * deliberately.
 *
 * Field names mirror the backend DTO `AccrualOverCreditExposureRow` /
 * `AccrualOverCreditExposureReportDto` exactly (System.Text.Json camelCase). Do
 * NOT rename or reshape — an ambiguous number here becomes a real dispute with a
 * real person.
 *
 * Backend contract:
 *   GET /api/v1/tenant/leave-entitlements/accrual-over-credit-exposure
 *       ?asOfDate=YYYY-MM-DD[&format=csv|xlsx]
 *   Permission: Leave.ConfigurePolicy
 *   No format   -> JSON (this report envelope, unwrapped from ApiResponse<T>)
 *   format=csv|xlsx -> file download
 */

import type { Schema } from '@core/api';

/** One (employee × leave type) row over-credited relative to its accrual frequency. */
export interface IAccrualOverCreditExposureRow {
  employeeId: string;
  employeeNo: string;
  employeeName: string;
  leaveTypeId: string;
  leaveTypeName: string;
  leaveYear: number;
  /** The type's configured accrual frequency (Monthly or Quarterly for any row that appears). */
  accrualFrequency: string;
  /** What the legacy accrual actually granted (sum of the Accrual ledger rows for the year). */
  creditedDays: number;
  /** Frequency-aware entitlement that SHOULD have accrued by the as-of date. */
  shouldHaveAccruedDays: number;
  /** The over-credit: creditedDays − shouldHaveAccruedDays (always > 0). */
  overCreditedDays: number;
  /** Whether the employee is still active (a terminated employee's over-credit reaches F&F). */
  isEmployeeActive: boolean;
}

/** Report envelope for the current tenant. */
export interface IAccrualOverCreditExposureReport {
  /** As-of date the figures were computed for (YYYY-MM-DD). */
  asOfDate: string;
  leaveYear: number;
  rows: IAccrualOverCreditExposureRow[];
}

// ─── Wire contract → view-model mapper (D-leave slice 2) ──────────────────────
//
// The exposure ROW is the GENERATED contract type, so a backend rename of an exposure field is a compile error
// here. NOTE (finding): the report ENVELOPE (`AccrualOverCreditExposureReportDto`) is ABSENT from the generated
// contract — the endpoint's 200 response is `content?: never` because the action returns JSON-or-file
// (`IActionResult`), so Swashbuckle emitted no schema for it. The two envelope scalars (`asOfDate`,
// `leaveYear`) are therefore read defensively in the service, while the substance — the per-row fields — is
// anchored to the generated `LeaveEntitlementsAccrualOverCreditExposureRow`.

export type AccrualExposureRowWire = Schema<'LeaveEntitlementsAccrualOverCreditExposureRow'>;

/** Maps a wire exposure row onto `IAccrualOverCreditExposureRow` (names align 1:1 with the backend DTO). */
export function mapAccrualExposureRow(
  w: AccrualExposureRowWire,
): IAccrualOverCreditExposureRow {
  return {
    employeeId: w.employeeId ?? '',
    employeeNo: w.employeeNo ?? '',
    employeeName: w.employeeName ?? '',
    leaveTypeId: w.leaveTypeId ?? '',
    leaveTypeName: w.leaveTypeName ?? '',
    leaveYear: w.leaveYear ?? 0,
    accrualFrequency: w.accrualFrequency ?? '',
    creditedDays: w.creditedDays ?? 0,
    shouldHaveAccruedDays: w.shouldHaveAccruedDays ?? 0,
    overCreditedDays: w.overCreditedDays ?? 0,
    isEmployeeActive: w.isEmployeeActive ?? false,
  };
}

/** Server-generated export formats supported by the endpoint. */
export type AccrualExposureExportFormat = 'csv' | 'xlsx';

/** A downloaded export blob plus the filename resolved from Content-Disposition. */
export interface IAccrualExposureExport {
  blob: Blob;
  filename: string;
}
