/**
 * ISSUE-021: Salary Grade models matching the backend API contract.
 *
 * The read response is the GENERATED contract type (`SalaryGradesSalaryGradeDto`), mapped onto the
 * `ISalaryGrade` view-model at the service seam so a backend rename becomes a compile error here.
 * The wire DTO also carries `createdAt`/`updatedAt`, which the FE does not render (dropped by the mapper).
 *
 * RESOLVED (B5, 2026-08-23) — the note that used to sit here said the update DTO had no `isActive`, so the
 * form's Active toggle was a silent no-op on save, and flagged it for a product decision. The decision was
 * to **honour the flag server-side** rather than delete the toggle: `UpdateSalaryGradeRequest.isActive`
 * now exists and the request payload is typed from it, so a future removal is a compile error rather than
 * a silent regression.
 *
 * That also closed a data-trap. `DELETE` was previously the only writer of the flag and there was no route
 * back, so a mis-clicked deactivation was permanent; the toggle now reactivates as well. The wire field is
 * NULLABLE — absent means "leave unchanged", so a caller posting the pre-B5 body cannot silently reactivate
 * a deactivated grade.
 *
 * The read DTO gained `referencingJobTitleCount` — job titles must resolve to an ACTIVE grade
 * (`JobTitleService.ValidateGradeAsync`), so deactivating a referenced grade breaks those titles' next
 * save. It is surfaced as a WARNING, not a block: retiring a grade mid-re-grade is legitimate.
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
  /**
   * How many job titles point at this grade. Drives the confirm shown before deactivating one that is in
   * use — the person clicking the toggle cannot otherwise see that consequence.
   */
  referencingJobTitleCount: number;
}

/**
 * Request body for create/update (the DTO minus `id`; `id` travels in the
 * PUT route). Sent to POST/PUT /api/v1/tenant/salary-grades.
 *
 * `isActive` is honoured on UPDATE only. Create always makes an active grade and the create wire DTO has
 * no such member, so the toggle is rendered in EDIT MODE ONLY — showing it on create would reproduce the
 * very no-op B5 fixed: flip it off, save, get a success toast, and get an active grade anyway.
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
 * The UPDATE request as the contract defines it. Typing the payload from the generated schema is what
 * stops `isActive` quietly becoming a no-op again: if the backend ever drops the member, this stops
 * compiling instead of silently ignoring the toggle the way it did before B5.
 */
export type SalaryGradeUpdateWire = Schema<'SalaryGradesUpdateSalaryGradeRequest'>;

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
    referencingJobTitleCount: w.referencingJobTitleCount ?? 0,
  };
}

/** Builds the UPDATE payload from the form model, typed by the contract. */
export function toSalaryGradeUpdateWire(
  request: ISalaryGradeRequest,
): SalaryGradeUpdateWire {
  return {
    code: request.code,
    name: request.name,
    minAmount: request.minAmount,
    midAmount: request.midAmount,
    maxAmount: request.maxAmount,
    currency: request.currency,
    description: request.description,
    isActive: request.isActive,
  };
}
