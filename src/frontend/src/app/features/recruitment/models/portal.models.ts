/**
 * US-REC-008: Candidate-portal models matching the backend API contract.
 *
 * The candidate portal is an ANONYMOUS, magic-link experience (no auth/role guard,
 * like the public careers pages). Access is controlled by a cryptographically
 * signed token carried in the URL (`/portal?token=...`). The FE reads it from the
 * URL and forwards it to the backend in the `X-Portal-Token` HEADER on every call
 * (kept out of the query string so it never lands in server logs / history); the
 * backend resolves the applicant + tenant from it (NFR-3 / NFR-4 / BR-6). All
 * requests are anonymous — no `withCredentials`, no session.
 *
 * Tenant is still resolved from the subdomain and carried by the tenantInterceptor
 * (X-Tenant-Subdomain header) + backend RLS, so the portal is tenant-scoped (AC-4 /
 * BR-4) even without a login.
 *
 * Responses are the BARE `T` — the global apiEnvelopeInterceptor (US-PLT-001)
 * unwraps the `ApiResponse<T>` envelope, so the service never touches `.data`.
 *
 * CONTRACT (reconciled with the backend `PortalController`; `apiBaseUrl` already
 * includes `/api/v1`. Endpoints are anonymous under `careers/portal`; the token
 * goes in the `X-Portal-Token` header):
 *
 *   GET  /careers/portal/dashboard                     -> IPortalDashboard          (AC-1/FR-2)
 *   GET  /careers/portal/offers/:offerId/document      -> Blob (Content-Disposition, FR-4)
 *   POST /careers/portal/offers/:offerId/respond
 *        body { accept: boolean }                       -> IPortalOffer              (AC-3/FR-5/BR-2)
 *   POST /careers/portal/request-link
 *        body { email }                                 -> 2xx                       (FR-8/BR-5)
 *
 * Backend dashboard DTO field names: `currentStage` (stage), `upcomingInterview`,
 * `activeOffer`, interview `interviewerNames: string[]`, offer `isDocumentAvailable`
 * + `canRespond`, timeline `occurredAt`. The service mapping tolerates both these
 * and the plainer aliases, so a DTO tweak is a one-place fix.
 *
 * An expired/invalid/cross-tenant token returns HTTP 401 (410 also treated as a
 * token error) — the FE shows the "request a new link" prompt (FR-8 / BR-5).
 *
 * SANITIZED VIEW (BR-3 / NFR-5): the backend MUST NOT expose rejection reasons,
 * interviewer comments, or scorecard details. The FE only renders what it receives.
 */

import { ApplicantStage } from './applicant.models';
import { InterviewType } from './interview.models';
import { OfferStatus, SalaryFrequency } from './offer.models';

// ─── Pipeline step indicator (AC-1 / §8) ─────────────────────────

/**
 * The visible pipeline stages shown in the candidate step indicator (AC-1):
 * Applied -> Screening -> Interview -> Offer -> Hired. `Rejected` is NOT a step on
 * the bar — a rejected application is rendered as a distinct terminal state so the
 * indicator stays a clean linear funnel (BR-3 sanitized view).
 */
export const PORTAL_STEPS: readonly ApplicantStage[] = [
  'Applied',
  'Screening',
  'Interview',
  'Offer',
  'Hired',
];

/** Human labels for each step (display order matches `PORTAL_STEPS`). */
export const PORTAL_STEP_LABELS: Record<string, string> = {
  Applied: 'Applied',
  Screening: 'Screening',
  Interview: 'Interview',
  Offer: 'Offer',
  Hired: 'Hired',
  Rejected: 'Closed',
};

/** Visual state of a single step in the indicator (§8 color coding). */
export type StepState = 'completed' | 'current' | 'future';

/** A resolved step for the indicator: label + visual state. */
export interface IPortalStep {
  stage: ApplicantStage;
  label: string;
  state: StepState;
}

// ─── Entities ────────────────────────────────────────────────────

/** One interviewer name shown to the candidate (NFR-5: names only, no comments). */
export interface IPortalInterviewer {
  name: string;
}

/**
 * Upcoming interview details visible to the candidate (AC-2 / FR-3). A sanitized
 * projection of the recruiter `IInterview` — no internal notes/scorecards (NFR-5).
 */
export interface IPortalInterview {
  id: string;
  roundNumber: number;
  interviewType: InterviewType;
  /** yyyy-MM-dd. */
  scheduledDate: string;
  /** HH:mm 24h. */
  startTime: string;
  durationMinutes: number;
  /** Set for in-person interviews. */
  location: string | null;
  /** Set for video interviews ("Join Meeting" link). */
  videoLink: string | null;
  interviewers: IPortalInterviewer[];
}

/**
 * Active offer details visible to the candidate (AC-3 / FR-5). A sanitized
 * projection of the recruiter `IOffer` — the candidate sees the headline terms and
 * can download the PDF + accept/decline.
 */
export interface IPortalOffer {
  id: string;
  offerReferenceNumber: string;
  status: OfferStatus;
  offeredPosition: string;
  departmentName: string | null;
  salaryAmount: number;
  currency: string;
  salaryFrequency: SalaryFrequency;
  benefitsSummary: string | null;
  /** yyyy-MM-dd. */
  startDate: string;
  /** yyyy-MM-dd. */
  expiryDate: string;
  /**
   * Whether the candidate can still accept/decline — AUTHORITATIVE from the
   * backend (offer is `Sent` + not yet acted on, BR-2 one-time). The FE reflects
   * this flag rather than deriving it, mirroring the scorecard `canViewOthers`
   * pattern. Defaults to false when absent (fail-closed).
   */
  canRespond: boolean;
  /** True when a PDF is available to download (FR-4). */
  hasDocument: boolean;
}

/** One sanitized timeline event for an application (FR-6 / BR-3). */
export interface IPortalTimelineEvent {
  /** ISO timestamp the event occurred. */
  at: string;
  /** Sanitized, candidate-safe label (e.g. "Moved to Screening"). */
  label: string;
}

/**
 * One application card on the portal dashboard (AC-1 / FR-2). Each carries the
 * vacancy summary, the current stage (drives the step indicator), and — when
 * present — the upcoming interview, the active offer, and the activity timeline.
 */
export interface IPortalApplication {
  id: string;
  applicationReferenceNumber: string;
  vacancyId: string;
  vacancyTitle: string;
  departmentName: string | null;
  /** ISO date the application was submitted (AC-1). */
  appliedAt: string;
  /** Current pipeline stage — drives the step indicator (AC-1). */
  stage: ApplicantStage;
  /** Upcoming interview, when scheduled (AC-2 / FR-3). */
  interview: IPortalInterview | null;
  /** Active offer, when one exists (AC-3 / FR-5). */
  offer: IPortalOffer | null;
  /** Sanitized activity log, newest first (FR-6). */
  timeline: IPortalTimelineEvent[];
}

/**
 * The full candidate-portal dashboard payload (§7 Output). Carries the candidate's
 * applications within the tenant resolved from the token (AC-4 / BR-4). The
 * candidate's display name is shown in the header greeting.
 */
export interface IPortalDashboard {
  applicantName: string;
  applicantEmail: string;
  applications: IPortalApplication[];
}

// ─── Helpers (pure — shared by component + tests) ────────────────

/**
 * Resolve the step indicator for an application's current stage (AC-1 / §8).
 * Steps before the current stage are `completed` (green), the matching stage is
 * `current` (blue), later stages are `future` (gray). A `Hired` application marks
 * every step completed. A `Rejected` (or otherwise off-funnel) stage leaves all
 * steps `future` — the card renders the terminal state separately (BR-3).
 */
export function buildSteps(stage: ApplicantStage | string): IPortalStep[] {
  const currentIndex = PORTAL_STEPS.indexOf(stage as ApplicantStage);
  return PORTAL_STEPS.map((s, i) => {
    let state: StepState;
    if (currentIndex < 0) {
      state = 'future';
    } else if (i < currentIndex) {
      state = 'completed';
    } else if (i === currentIndex) {
      state = 'current';
    } else {
      state = 'future';
    }
    return { stage: s, label: PORTAL_STEP_LABELS[s] ?? s, state };
  });
}

/** Tailwind classes for a step's circle by visual state (§8). */
export function stepCircleClass(state: StepState): string {
  switch (state) {
    case 'completed':
      return 'bg-green-500 text-white';
    case 'current':
      return 'bg-blue-600 text-white ring-4 ring-blue-100';
    default:
      return 'bg-neutral-200 text-neutral-500';
  }
}

/** Tailwind classes for the connector line leading INTO a step (§8). */
export function stepConnectorClass(state: StepState): string {
  // The line before a completed/current step is "done"; before a future step gray.
  return state === 'future' ? 'bg-neutral-200' : 'bg-green-500';
}

/** True when the application is in the terminal Rejected/closed state (BR-3). */
export function isRejected(stage: ApplicantStage | string): boolean {
  return stage === 'Rejected';
}

/** Human label for the interview type (reuses the recruiter labels, FR-3). */
export function portalInterviewTypeLabel(type: InterviewType | string): string {
  switch (type) {
    case 'InPerson':
      return 'In-person';
    case 'Video':
      return 'Video';
    case 'Phone':
      return 'Phone';
    default:
      return String(type);
  }
}

/**
 * Whether the candidate can still respond to the offer (AC-3 / BR-2). Reflects the
 * backend's authoritative `canRespond` flag (set only while the offer is `Sent` and
 * not yet acted on — one-time, BR-2). The FE never derives this; the backend
 * re-checks on POST regardless.
 */
export function canRespondToPortalOffer(offer: IPortalOffer | null): boolean {
  return !!offer && offer.canRespond;
}

/** Compact compensation string for the offer card, e.g. "USD 120,000 / yr". */
export function formatPortalCompensation(offer: {
  salaryAmount: number;
  currency: string;
  salaryFrequency: SalaryFrequency;
}): string {
  const amount =
    typeof offer.salaryAmount === 'number'
      ? offer.salaryAmount.toLocaleString('en-US')
      : '';
  const suffix =
    offer.salaryFrequency === 'Annual'
      ? '/ yr'
      : offer.salaryFrequency === 'Monthly'
        ? '/ mo'
        : offer.salaryFrequency === 'Weekly'
          ? '/ wk'
          : offer.salaryFrequency === 'Hourly'
            ? '/ hr'
            : '';
  return `${offer.currency ?? ''} ${amount} ${suffix}`.replace(/\s+/g, ' ').trim();
}
