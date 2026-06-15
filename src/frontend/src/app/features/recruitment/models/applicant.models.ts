/**
 * US-REC-002: Applicant + public-careers models matching the backend API contract.
 *
 * Backend endpoints (assumed contract — backend agent building in parallel; ADAPT
 * here if it differs — the mapping is intentionally kept in ONE place):
 *
 *   PUBLIC (anonymous — public careers page, NO auth/role guard):
 *     GET  /api/v1/careers/vacancies                 - open vacancies (public listing)
 *     GET  /api/v1/careers/vacancies/:id             - single public vacancy detail
 *     POST /api/v1/careers/vacancies/:id/apply       - multipart apply -> IApplicant (AC-1)
 *
 *   INTERNAL (authenticated employee — AC-4 / FR-8):
 *     POST /api/v1/recruitment/vacancies/:id/applicants   - multipart internal apply -> IApplicant
 *
 * `apiBaseUrl` already includes `/api/v1`. The public listing/detail reuse the
 * existing careers endpoints; the recruiter-facing applicant list (FR-7) is added
 * by a later story and is out of scope here.
 *
 * The backend stamps tenant_id, applicationReferenceNumber, stage, source, audit
 * fields, and the sanitized/renamed resume file (BR-3) server-side — the FE never
 * sends them.
 */

// ─── Enums ────────────────────────────────────────────────────

/** Pipeline stage of an application (FR-6 — created at `Applied`). */
export type ApplicantStage =
  | 'Applied'
  | 'Screening'
  | 'Interview'
  | 'Offer'
  | 'Hired'
  | 'Rejected';

/** Where the application originated (§7 Output). Matches C# `ApplicationSource` (US-PLT-003). */
export type ApplicantSource = 'Public' | 'Internal' | 'Referral';

// ─── File-upload constraints (AC-2 / BR-4) ────────────────────

/** Max resume size in bytes (25 MB — AC-2 / FR-1). */
export const RESUME_MAX_BYTES = 25 * 1024 * 1024;

/** Human-readable max size for the upload zone copy. */
export const RESUME_MAX_LABEL = '25 MB';

/**
 * Allowed resume MIME types (BR-4). Note: browsers sometimes report an empty
 * string or a generic type for `.doc`, so the extension check below is the
 * authoritative client-side guard, with the MIME list as a secondary signal.
 */
export const RESUME_ALLOWED_MIME: readonly string[] = [
  'application/pdf',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
  'application/msword',
];

/** Allowed resume extensions (lowercase, with dot) — the primary client check. */
export const RESUME_ALLOWED_EXTENSIONS: readonly string[] = [
  '.pdf',
  '.docx',
  '.doc',
];

/** Display string for accepted formats shown in the upload zone (§8). */
export const RESUME_ACCEPTED_LABEL = 'PDF, DOCX or DOC';

/** `accept` attribute value for the hidden <input type="file">. */
export const RESUME_ACCEPT_ATTR = RESUME_ALLOWED_EXTENSIONS.join(',');

/** Max cover-letter length (FR-1). */
export const COVER_LETTER_MAX = 2000;

// ─── Entities ────────────────────────────────────────────────

/**
 * Public-facing vacancy projection for the careers page (§8). A trimmed view of
 * the recruiter vacancy — only fields safe/relevant to anonymous applicants.
 */
export interface IPublicVacancy {
  id: string;
  referenceNumber: string;
  title: string;
  departmentName: string | null;
  jobTitleName: string | null;
  employmentType: string | null;
  locationName: string | null;
  /** Rich text (HTML). Rendered via Angular's default sanitizer (NFR-4). */
  description: string | null;
  /** Rich text (HTML). Rendered via Angular's default sanitizer (NFR-4). */
  qualifications: string | null;
  salaryMin: number | null;
  salaryMax: number | null;
  currency: string | null;
  /** ISO date (yyyy-MM-dd) or null — applications close after this (BR-6). */
  applicationDeadline: string | null;
  postedAt: string;
}

/**
 * Applicant record returned on successful submission (§7 Output / AC-1).
 * The confirmation screen shows `applicationReferenceNumber`.
 */
export interface IApplicant {
  id: string;
  /** Human-readable reference shown on the confirmation screen (AC-1). */
  applicationReferenceNumber: string;
  vacancyId: string;
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;
  coverLetter: string | null;
  /** Sanitized/renamed file name the backend stored (BR-3). */
  resumeFileName: string | null;
  stage: ApplicantStage;
  source: ApplicantSource;
  isInternal: boolean;
  appliedAt: string;
}

/**
 * Application form payload (FR-1). Sent as multipart/form-data alongside the
 * resume file — see `CareersService.buildFormData`. The `resume` file is carried
 * separately so the service can attach it + report upload progress.
 */
export interface IApplicationRequest {
  firstName: string;
  lastName: string;
  email: string;
  phone: string | null;
  coverLetter: string | null;
}

/** Query params for the public open-vacancy listing (§8 search/filter). */
export interface IPublicVacancyListParams {
  search?: string;
  departmentName?: string;
  locationName?: string;
  employmentType?: string;
}

/** Tenant branding shown on the careers page header (§8 logo + primary color). */
export interface ICareersBranding {
  name: string;
  logoUrl?: string | null;
  primaryColor?: string | null;
}

/** Typed error body the backend returns for rejected applications (shown verbatim). */
export interface IApplicantErrorResponse {
  message: string;
  /** `duplicate` for AC-3, `file_rejected` for AC-2, etc. */
  code?: string;
}

/** Discriminates the duplicate-application error (AC-3) from generic failures. */
export const DUPLICATE_APPLICATION_CODE = 'duplicate';
