/**
 * US-REC-001: Recruitment vacancy models matching the backend API contract.
 *
 * Backend endpoints (assumed contract — backend agent building in parallel):
 *   GET  /api/v1/recruitment/vacancies            - paged list (status/department filter)
 *   GET  /api/v1/recruitment/vacancies/:id        - single vacancy
 *   POST /api/v1/recruitment/vacancies            - create (Draft)
 *   PUT  /api/v1/recruitment/vacancies/:id        - update
 *   POST /api/v1/recruitment/vacancies/:id/publish - Draft/On Hold -> Open
 *   POST /api/v1/recruitment/vacancies/:id/close   - Open/On Hold -> Closed
 *   POST /api/v1/recruitment/vacancies/:id/status  - generic status change
 *
 * NOTE: `apiBaseUrl` already includes `/api/v1`, so the resource is
 * `${apiBaseUrl}/recruitment/vacancies`. All requests are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header) and use withCredentials for the
 * httpOnly cookie auth. The backend stamps tenant_id, reference number, slug, and
 * audit fields server-side (FR-7, §7) — the FE never sends them.
 */

// ─── Enums ────────────────────────────────────────────────────

/** Vacancy lifecycle status (FR-2). */
export type VacancyStatus =
  | 'Draft'
  | 'Open'
  | 'On Hold'
  | 'Closed'
  | 'Cancelled';

export const VACANCY_STATUS_OPTIONS: VacancyStatus[] = [
  'Draft',
  'Open',
  'On Hold',
  'Closed',
  'Cancelled',
];

/** Employment type for the position (FR-1). Mirrors Core HR EmploymentType. */
export type VacancyEmploymentType =
  | 'Full-Time'
  | 'Part-Time'
  | 'Contract'
  | 'Intern';

export const VACANCY_EMPLOYMENT_TYPE_OPTIONS: VacancyEmploymentType[] = [
  'Full-Time',
  'Part-Time',
  'Contract',
  'Intern',
];

/**
 * Tailwind utility-class bundle per status for the color-coded badge (§8):
 * Draft=gray, Open=green, On Hold=amber, Closed=red, Cancelled=red.
 */
export const VACANCY_STATUS_BADGE: Record<VacancyStatus, string> = {
  Draft: 'bg-neutral-100 text-neutral-700 ring-neutral-200',
  Open: 'bg-green-100 text-green-700 ring-green-200',
  'On Hold': 'bg-amber-100 text-amber-700 ring-amber-200',
  Closed: 'bg-red-100 text-red-700 ring-red-200',
  Cancelled: 'bg-red-100 text-red-700 ring-red-200',
};

// ─── Entities ────────────────────────────────────────────────

/**
 * Vacancy record returned by the API (§7 Output). Field names are camelCase to
 * match the typical backend DTO. Reference number / slug / filled count are
 * server-managed (BR-4, FR-5).
 */
export interface IVacancy {
  id: string;
  /** Auto-generated per tenant + year, e.g. "VAC-2026-0001" (§7, §10). */
  referenceNumber: string;
  title: string;
  departmentId: string | null;
  departmentName: string | null;
  jobTitleId: string | null;
  jobTitleName: string | null;
  employmentType: VacancyEmploymentType | null;
  locationId: string | null;
  locationName: string | null;
  hiringManagerId: string | null;
  hiringManagerName: string | null;
  /** Maximum positions to fill (BR-4); integer >= 1 when set. */
  headcount: number | null;
  /** Tracked as applicants convert to employees (BR-4); server-managed. */
  filledCount: number;
  salaryMin: number | null;
  salaryMax: number | null;
  currency: string | null;
  /** Rich text (HTML). Rendered via Angular's default sanitizer (NFR-4). */
  description: string | null;
  /** Rich text (HTML). Rendered via Angular's default sanitizer (NFR-4). */
  qualifications: string | null;
  /** ISO date (yyyy-MM-dd) or null (FR-1 optional). */
  applicationDeadline: string | null;
  status: VacancyStatus;
  /** SEO-friendly URL slug for the public careers page (FR-5); server-managed. */
  slug: string | null;
  createdAt: string;
  updatedAt?: string;
}

/**
 * Create/update payload (§7 Input). The FE never sends reference number, slug,
 * filledCount, status, or tenant_id — the backend owns those. To create a draft
 * the only hard requirement is `title` (BR-2 publish rules are enforced separately
 * by the backend on the publish endpoint).
 */
export interface IVacancyRequest {
  title: string;
  departmentId: string | null;
  jobTitleId: string | null;
  employmentType: VacancyEmploymentType | null;
  locationId: string | null;
  hiringManagerId: string | null;
  headcount: number | null;
  salaryMin: number | null;
  salaryMax: number | null;
  currency: string | null;
  description: string | null;
  qualifications: string | null;
  applicationDeadline: string | null;
}

/** Query params for the paged vacancy list (NFR-1, §8 filter by status). */
export interface IVacancyListParams {
  status?: VacancyStatus;
  departmentId?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

/** Body for the generic status-change endpoint. */
export interface IVacancyStatusRequest {
  status: VacancyStatus;
}

/** Paginated response wrapper from the backend (mirrors Core HR IPaginatedResponse). */
export interface IVacancyPage {
  data: IVacancy[];
  total: number;
  page: number;
  pageSize: number;
}

/**
 * Lightweight option used by every dropdown (department / job title / location /
 * hiring manager). Keeping a single shape means the lookup mapping lives in ONE
 * place in the service — a one-line fix if a master-data DTO differs.
 */
export interface ILookupOption {
  id: string;
  label: string;
  /** Optional sub-label (e.g. job title under an employee name) for richer rows. */
  sublabel?: string | null;
}

/** Typed error body the backend returns for rejected actions (shown verbatim). */
export interface IVacancyErrorResponse {
  message: string;
  code?: string;
}
