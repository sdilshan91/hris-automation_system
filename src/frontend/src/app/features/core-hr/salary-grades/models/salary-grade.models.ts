/**
 * ISSUE-021: Salary Grade models matching the backend API contract.
 *
 * The read response is the GENERATED contract type (`SalaryGradesSalaryGradeDto`), mapped onto the
 * `ISalaryGrade` view-model at the service seam so a backend rename becomes a compile error here.
 * The wire DTO also carries `createdAt`/`updatedAt`, which the FE does not render (dropped by the mapper).
 *
 * CONTRACT NOTE (D-core-hr slice 1): the create/update REQUEST DTOs
 * (`SalaryGradesCreateSalaryGradeRequest` / `…UpdateSalaryGradeRequest`) have **no `isActive` member** —
 * the API ignores an `isActive` sent on create/update, so the form's Active toggle is a silent no-op on
 * save (deactivation is the DELETE route). `ISalaryGradeRequest` below still carries `isActive` and the
 * form still sends it; removing it (and the dead toggle) needs a product decision and is reported, not
 * done here. See the D-core-hr slice-1 report.
 */

import type { Schema } from '@core/api';

/** Salary grade entity returned by the API */
export interface ISalaryGrade {
  id: string;
  code: string;
  name: string;
  minAmount: number;
  /** Optional mid-point of the band. May be null. */
  midAmount: number | null;
  maxAmount: number;
  /** ISO 4217 currency code (3 chars, e.g. "USD") */
  currency: string;
  description: string | null;
  isActive: boolean;
}

/**
 * Request body for create/update (the DTO minus `id`; `id` travels in the
 * PUT route). Sent to POST/PUT /api/v1/tenant/salary-grades.
 */
export interface ISalaryGradeRequest {
  code: string;
  name: string;
  minAmount: number;
  midAmount: number | null;
  maxAmount: number;
  currency: string;
  description: string | null;
  isActive: boolean;
}

/**
 * Error response shape from the backend for salary grade operations.
 * 422 carries an `error.code` (e.g. 'invalid_grade'); a duplicate `code`
 * surfaces as 409/422 with code 'duplicate_code'.
 */
export interface ISalaryGradeErrorResponse {
  message?: string;
  code?: 'invalid_grade' | 'duplicate_code' | string;
}

// ─── Wire contract → view-model mapper (D-core-hr slice 1) ────────────────────

export type SalaryGradeWire = Schema<'SalaryGradesSalaryGradeDto'>;

/**
 * Maps the wire `SalaryGradesSalaryGradeDto` onto the `ISalaryGrade` view-model. Field names match
 * one-to-one (no renames); the mapper's job is to default the all-optional wire fields and drop the
 * wire-only `createdAt`/`updatedAt` the FE never renders.
 */
export function mapSalaryGrade(w: SalaryGradeWire): ISalaryGrade {
  return {
    id: w.id ?? '',
    code: w.code ?? '',
    name: w.name ?? '',
    minAmount: w.minAmount ?? 0,
    midAmount: w.midAmount ?? null,
    maxAmount: w.maxAmount ?? 0,
    currency: w.currency ?? '',
    description: w.description ?? null,
    isActive: w.isActive ?? true,
  };
}
