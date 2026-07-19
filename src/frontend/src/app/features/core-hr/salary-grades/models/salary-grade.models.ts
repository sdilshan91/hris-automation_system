/**
 * ISSUE-021: Salary Grade models matching the backend API contract.
 *
 * DTO over the wire is camelCase:
 *   SalaryGradeDto { id, code, name, minAmount, midAmount?, maxAmount,
 *                    currency, description?, isActive }
 */

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
