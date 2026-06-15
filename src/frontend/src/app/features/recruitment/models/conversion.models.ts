/**
 * US-REC-010: Applicant→Employee conversion models matching the backend API
 * contract.
 *
 * The prefill DTO, the convert request body, the convert result, and the
 * blocked-conversion error codes live HERE so a backend DTO mismatch is a
 * one-place fix. Responses are the BARE `T` — the global apiEnvelopeInterceptor
 * (US-PLT-001) unwraps the `ApiResponse<T>` envelope, so the service never
 * touches `.data`.
 *
 * CONTRACT (PROPOSED — reconcile with the backend agent; `apiBaseUrl` already
 * includes `/api/v1`):
 *
 *   GET  /recruitment/applicants/:applicantId/conversion-prefill
 *        -> IConversionPrefill (FR-2/FR-3/AC-1; data mapped from application + offer,
 *           plus a suggested auto-generated employee number, FR-4)
 *   POST /recruitment/applicants/:applicantId/convert  body IConvertEmployeeRequest
 *        -> IConvertResult (AC-2/AC-4; atomic — creates the employee, links the
 *           applicant, increments the vacancy filled count)
 *
 * All requests use `withCredentials` (httpOnly cookie) and are tenant-scoped via
 * the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-5 / NFR-2).
 *
 * Enum string values MUST match the C# member names (PascalCase) — the API uses a
 * global JsonStringEnumConverter (US-PLT-003). `EmploymentType` is reused from the
 * Core HR module (PascalCase: Full-Time/Part-Time/Contract/Intern).
 */

import { EmploymentType } from '../../core-hr/employees/models/employee.models';

// ─── Prefill (FR-2 / FR-3 / AC-1) ─────────────────────────────

/**
 * Pre-filled employee data the backend maps from the application + the accepted
 * offer (§7 Data Mapping). Every field is editable on the form (FR-3). Fields the
 * backend could populate are marked "auto-filled" in the UI when present.
 *
 * Lookup ids (department/jobTitle/reportingManager) come with display-name
 * companions so the form can show the pre-filled value without an extra round-trip.
 */
export interface IConversionPrefill {
  applicantId: string;
  /** True once already converted (BR-2) — the FE blocks a second conversion. */
  alreadyConverted: boolean;
  /** Set when alreadyConverted — links to the existing employee (AC-4). */
  convertedToEmployeeId?: string | null;

  // Personal info (from application)
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;

  // Employment details (from offer)
  jobTitleId: string | null;
  jobTitleName: string | null;
  departmentId: string | null;
  departmentName: string | null;
  reportingManagerId: string | null;
  reportingManagerName: string | null;
  /** Offer start date → date of joining (BR-4); editable. yyyy-MM-dd. */
  dateOfJoining: string | null;
  probationMonths: number | null;

  // Compensation (from offer)
  salaryAmount: number | null;
  currency: string | null;

  /**
   * Suggested employee number following the tenant pattern (FR-4, e.g.
   * "EMP-2026-0042"). The HR Officer may override it.
   */
  suggestedEmployeeNumber: string | null;

  /** Vacancy headcount info for the success/summary copy (AC-4), optional. */
  vacancyId?: string | null;
  vacancyTitle?: string | null;
  vacancyFilledCount?: number | null;
  vacancyHeadcount?: number | null;
}

// ─── Convert request (AC-2) ───────────────────────────────────

/**
 * Body for the conversion POST (AC-2). The HR Officer's reviewed/edited values for
 * the new employee. `applicantId` is in the path; the backend resolves the offer,
 * stamps tenant/audit, links the applicant, and updates the vacancy atomically (NFR-3).
 */
export interface IConvertEmployeeRequest {
  // Personal
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;

  // Employment
  /** Override or accept the suggested number (FR-4). Empty → backend auto-generates. */
  employeeNumber: string | null;
  jobTitleId: string;
  departmentId: string;
  reportingManagerId: string | null;
  employmentType: EmploymentType;
  /** Work location (required, FR-2 remaining field). */
  workLocationId: string;
  /** yyyy-MM-dd (BR-4 — defaults to the offer start date). */
  dateOfJoining: string;

  // Compensation
  salaryAmount: number | null;
  currency: string | null;
}

// ─── Convert result (AC-2 / AC-4) ─────────────────────────────

/**
 * Result of a successful conversion (AC-2/AC-4). Carries the new employee id so the
 * FE can deep-link to the Core HR profile, plus the updated vacancy fill ratio.
 */
export interface IConvertResult {
  /** New Core HR employee id — links to /employees/:id (AC-4). */
  employeeId: string;
  employeeNumber: string;
  firstName: string;
  lastName: string;
  /** Whether a linked user account was auto-created (FR-5/AC-3/BR-7), if reported. */
  userAccountCreated?: boolean;
  vacancyFilledCount?: number | null;
  vacancyHeadcount?: number | null;
  /** True when filling this seat closed the vacancy (FR-7/BR-5), if reported. */
  vacancyClosed?: boolean;
}

// ─── Blocked-conversion errors (BR-2 / BR-3) ──────────────────

/**
 * Typed error body for a blocked conversion (shown verbatim). The backend returns a
 * `code` so the FE can tailor the banner without string-matching the message.
 */
export interface IConversionErrorResponse {
  message: string;
  /** `already_converted` (BR-2) | `plan_limit_reached` (BR-3) | other. */
  code?: string;
}

/** BR-2: the applicant was already converted to an employee. */
export const ALREADY_CONVERTED_CODE = 'already_converted';

/** BR-3: converting would exceed the subscription plan's employee limit. */
export const PLAN_LIMIT_CODE = 'plan_limit_reached';
