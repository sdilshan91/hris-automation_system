/**
 * US-REC-007: Offer-letter models matching the backend API contract.
 *
 * The offer shape, the create/generate request body, the respond/withdraw bodies,
 * and the status badge styling live HERE so a backend DTO mismatch is a one-place
 * fix. Responses are the BARE `T` — the global apiEnvelopeInterceptor (US-PLT-001)
 * unwraps the `ApiResponse<T>` envelope, so the service never touches `.data`.
 *
 * CONTRACT (PROPOSED — no backend offer endpoints existed when this FE was built;
 * this is the FE's contract for the backend agent to MATCH. Reconcile in
 * offer.service.ts — `apiBaseUrl` already includes `/api/v1`):
 *
 *   POST /recruitment/applicants/:applicantId/offers  body IOfferRequest
 *        -> IOffer (status Draft, FR-1/AC-1; backend generates the PDF + ref no.)
 *   GET  /recruitment/applicants/:applicantId/offers   -> IOffer[]   (offer history/versions)
 *   GET  /recruitment/offers/:id                        -> IOffer
 *   GET  /recruitment/offers/:id/pdf                    -> Blob (Content-Disposition filename, FR-4)
 *   POST /recruitment/offers/:id/send                   -> IOffer (status Sent, AC-2/FR-5)
 *   POST /recruitment/offers/:id/respond  body IOfferResponseRequest
 *        -> IOffer (status Accepted|Declined, AC-3; Accept advances applicant to Hired, BR-3)
 *   POST /recruitment/offers/:id/withdraw body { reason? }
 *        -> IOffer (status Withdrawn, FR-8 — only before acceptance)
 *
 * All requests use `withCredentials` (httpOnly cookie) and are tenant-scoped via
 * the tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-5 / NFR-2).
 *
 * Enum string values MUST match the C# member names (PascalCase) — the API uses a
 * global JsonStringEnumConverter (US-PLT-003). Do NOT use lowercase/kebab.
 */

// ─── Enums ────────────────────────────────────────────────────

/**
 * Offer lifecycle status (FR-6). Matches the C# `OfferStatus` member names
 * (PascalCase, US-PLT-003). Source of truth for the §8 status badges.
 */
export type OfferStatus =
  | 'Draft'
  | 'Sent'
  | 'Accepted'
  | 'Declined'
  | 'Expired'
  | 'Withdrawn';

/**
 * Salary pay frequency for the compensation section (FR-1). Matches the C#
 * `SalaryFrequency` member names (PascalCase, US-PLT-003). Reconcile the member
 * set with the backend enum — these are the conventional values.
 */
export type SalaryFrequency =
  | 'Annual'
  | 'Monthly'
  | 'Weekly'
  | 'Hourly';

/** Applicant's recorded response to a sent offer (AC-3). */
export type OfferResponse = 'Accepted' | 'Declined';

// ─── Display tables (single source of truth) ──────────────────

/**
 * §8 status badge classes. Draft=gray, Sent=blue, Accepted=green, Declined=red,
 * Expired=orange, Withdrawn=gray (rendered with a strikethrough in the template).
 */
export const OFFER_STATUS_BADGE: Record<OfferStatus, string> = {
  Draft: 'bg-neutral-100 text-neutral-700 ring-neutral-200',
  Sent: 'bg-blue-50 text-blue-700 ring-blue-200',
  Accepted: 'bg-green-50 text-green-700 ring-green-200',
  Declined: 'bg-red-50 text-red-700 ring-red-200',
  Expired: 'bg-orange-50 text-orange-700 ring-orange-200',
  Withdrawn: 'bg-neutral-100 text-neutral-500 ring-neutral-200 line-through',
};

/** Human labels for the status (cards/timeline). */
export const OFFER_STATUS_LABELS: Record<OfferStatus, string> = {
  Draft: 'Draft',
  Sent: 'Sent',
  Accepted: 'Accepted',
  Declined: 'Declined',
  Expired: 'Expired',
  Withdrawn: 'Withdrawn',
};

/** Salary-frequency options for the compensation selector (FR-1), in display order. */
export const SALARY_FREQUENCY_OPTIONS: readonly {
  value: SalaryFrequency;
  label: string;
  /** Short suffix shown next to the amount, e.g. "/ yr". */
  suffix: string;
}[] = [
  { value: 'Annual', label: 'Per year', suffix: '/ yr' },
  { value: 'Monthly', label: 'Per month', suffix: '/ mo' },
  { value: 'Weekly', label: 'Per week', suffix: '/ wk' },
  { value: 'Hourly', label: 'Per hour', suffix: '/ hr' },
];

/**
 * Common ISO-4217 currency codes for the currency selector (FR-1). The control is
 * a free-typed/select hybrid so any code is accepted; this is just the shortlist.
 */
export const CURRENCY_OPTIONS: readonly string[] = [
  'USD',
  'EUR',
  'GBP',
  'LKR',
  'INR',
  'AUD',
  'CAD',
  'SGD',
];

// ─── Entities ─────────────────────────────────────────────────

/**
 * A generated offer letter (§7 Output), matching the backend `OfferDto`. The
 * backend stamps `id`, `tenant_id`, `offerReferenceNumber`, `status`, `version`,
 * `pdfStorageKey`, and the audit/sent/responded timestamps server-side; the PDF is
 * generated server-side from the template (FR-2) and streamed via the pdf endpoint.
 */
export interface IOffer {
  id: string;
  /** Human-readable reference number stamped by the backend (§7). */
  offerReferenceNumber: string;
  applicantId: string;
  vacancyId: string;
  status: OfferStatus;
  /** PascalCase enum name echoed by the backend (display convenience). */
  statusName?: string | null;

  // Position
  offeredPosition: string;
  departmentId?: string | null;
  departmentName?: string | null;
  reportingManagerId?: string | null;
  reportingManagerName?: string | null;

  // Compensation
  salaryAmount: number;
  currency: string;
  salaryFrequency: SalaryFrequency;
  benefitsSummary?: string | null;

  // Timeline
  /** ISO date (yyyy-MM-dd). */
  startDate: string;
  /** ISO date (yyyy-MM-dd) — mandatory; default +7d (BR-6). */
  expiryDate: string;
  probationMonths?: number | null;

  // Additional
  customClauses?: string | null;

  /** Increments when a new version supersedes a prior offer (FR-9). */
  version: number;
  /** ISO timestamp the offer was emailed to the applicant (AC-2), or null. */
  sentAt?: string | null;
  /** ISO timestamp the applicant responded (AC-3), or null. */
  respondedAt?: string | null;
  /** The recorded response when responded (AC-3), or null. */
  response?: OfferResponse | null;
  /** ISO created timestamp (generated). */
  createdAt: string;
}

/**
 * Offer create/generate request (FR-1 / AC-1). `applicantId` is in the path; the
 * body carries the offer details. The backend resolves the vacancy from the
 * applicant, stamps tenant/ref/version, generates the PDF, and returns the offer in
 * `Draft`. The FE never sends server-managed fields.
 */
export interface IOfferRequest {
  /** Optional tenant offer-letter template id (FR-1); backend uses a default when omitted. */
  templateId?: string | null;
  offeredPosition: string;
  departmentId?: string | null;
  reportingManagerId?: string | null;
  salaryAmount: number;
  currency: string;
  salaryFrequency: SalaryFrequency;
  benefitsSummary?: string | null;
  startDate: string;
  expiryDate: string;
  probationMonths?: number | null;
  customClauses?: string | null;
}

/** Body for recording an applicant's response to a sent offer (AC-3). */
export interface IOfferResponseRequest {
  response: OfferResponse;
  /** Optional note (e.g. decline reason). */
  notes?: string | null;
}

// ─── Helpers ──────────────────────────────────────────────────

/** Badge classes for a status, falling back to the Draft style for unknowns. */
export function offerStatusBadge(status: OfferStatus | string): string {
  return OFFER_STATUS_BADGE[status as OfferStatus] ?? OFFER_STATUS_BADGE.Draft;
}

/** Human label for a status, tolerating unknown values. */
export function offerStatusLabel(status: OfferStatus | string): string {
  return OFFER_STATUS_LABELS[status as OfferStatus] ?? String(status);
}

/** Short pay-frequency suffix (e.g. "/ yr"); empty for unknowns. */
export function salaryFrequencySuffix(freq: SalaryFrequency | string): string {
  return (
    SALARY_FREQUENCY_OPTIONS.find((o) => o.value === freq)?.suffix ?? ''
  );
}

/**
 * Whether the offer can still be SENT (FR-5): only a Draft offer is sendable. A
 * withdrawn/declined/expired offer requires a new version (BR-7).
 */
export function canSendOffer(offer: IOffer | null): boolean {
  return offer?.status === 'Draft';
}

/**
 * Whether a response (Accept/Decline) can be recorded (AC-3): only while the offer
 * is `Sent`. The backend re-checks authoritatively.
 */
export function canRespondToOffer(offer: IOffer | null): boolean {
  return offer?.status === 'Sent';
}

/**
 * Whether the offer can be WITHDRAWN (FR-8): allowed any time BEFORE acceptance,
 * i.e. while Draft or Sent. Accepted/Declined/Expired/Withdrawn cannot be withdrawn.
 */
export function canWithdrawOffer(offer: IOffer | null): boolean {
  return offer?.status === 'Draft' || offer?.status === 'Sent';
}

/**
 * Default offer expiry date — `from` plus `days` (BR-6 default 7), as a yyyy-MM-dd
 * string. Pure so the form and tests can share it.
 */
export function defaultExpiryDate(from: Date = new Date(), days = 7): string {
  const d = new Date(from.getTime());
  d.setDate(d.getDate() + days);
  return toIsoDate(d);
}

/** A Date as a yyyy-MM-dd string (local), for date input controls. */
export function toIsoDate(d: Date): string {
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

/**
 * Compact formatted compensation string for cards/preview, e.g. "USD 120,000 / yr".
 * Tolerates a missing amount.
 */
export function formatCompensation(offer: {
  salaryAmount: number;
  currency: string;
  salaryFrequency: SalaryFrequency;
}): string {
  const amount =
    typeof offer.salaryAmount === 'number'
      ? offer.salaryAmount.toLocaleString('en-US')
      : '';
  const suffix = salaryFrequencySuffix(offer.salaryFrequency);
  return `${offer.currency ?? ''} ${amount} ${suffix}`.replace(/\s+/g, ' ').trim();
}
