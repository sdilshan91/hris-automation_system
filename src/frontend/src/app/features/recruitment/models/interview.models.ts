/**
 * US-REC-005: Interview scheduling models matching the backend API contract.
 *
 * The interview shape, the schedule/reschedule request bodies, and the calendar
 * filters live HERE so a backend DTO mismatch is a one-place fix. Responses are
 * the BARE `T` — the global apiEnvelopeInterceptor (US-PLT-001) unwraps the
 * `ApiResponse<T>` envelope, so the service never touches `.data`.
 *
 * CONTRACT (assumed — reconcile with backend; `apiBaseUrl` already includes
 * `/api/v1`):
 *
 *   POST /recruitment/applicants/:applicantId/interviews
 *        body IInterviewRequest -> IScheduleResult { interview, warnings[] }   (FR-1/FR-7)
 *   GET  /recruitment/applicants/:applicantId/interviews
 *        -> IInterview[]                                                        (AC-4 rounds)
 *   PUT  /recruitment/interviews/:id
 *        body IInterviewRequest -> IScheduleResult { interview, warnings[] }    (AC-3 reschedule)
 *   POST /recruitment/interviews/:id/cancel
 *        body { reason? } -> IInterview                                         (AC-3 cancel)
 *   GET  /recruitment/interviews?interviewerId=&vacancyId=&from=&to=&status=
 *        -> IInterview[] (tenant-wide agenda/calendar, FR-5)                    (FR-5/FR-6)
 *
 * All requests use withCredentials (httpOnly cookie) and are tenant-scoped via the
 * tenantInterceptor (X-Tenant-Subdomain header) + backend RLS (AC-5 / NFR-2).
 *
 * Soft conflict warnings (interviewer double-booked, FR-7) come back as
 * `warnings[]` on a SUCCESSFUL 2xx schedule/reschedule — the operation still
 * succeeds; the FE surfaces them as a toast.
 */

// ─── Enums ────────────────────────────────────────────────────

/** Interview delivery type (FR-1). Conditional required fields hang off this. Matches C# `InterviewType` (US-PLT-003). */
export type InterviewType = 'InPerson' | 'Video' | 'Phone';

/** Interview lifecycle status (FR-6). */
export type InterviewStatus = 'Scheduled' | 'Completed' | 'Cancelled' | 'NoShow';

/** Display order for the type dropdown (FR-1). */
export const INTERVIEW_TYPE_OPTIONS: readonly {
  value: InterviewType;
  label: string;
}[] = [
  { value: 'InPerson', label: 'In-person' },
  { value: 'Video', label: 'Video' },
  { value: 'Phone', label: 'Phone' },
];

/** Human labels for interview types (cards/badges). */
export const INTERVIEW_TYPE_LABELS: Record<InterviewType, string> = {
  InPerson: 'In-person',
  Video: 'Video',
  Phone: 'Phone',
};

/** All statuses for the calendar filter dropdown (FR-6). */
export const INTERVIEW_STATUSES: readonly InterviewStatus[] = [
  'Scheduled',
  'Completed',
  'Cancelled',
  'NoShow',
];

/** Human labels for statuses (the enum uses NoShow; show "No-show"). */
export const INTERVIEW_STATUS_LABELS: Record<InterviewStatus, string> = {
  Scheduled: 'Scheduled',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
  NoShow: 'No-show',
};

/**
 * Tailwind badge classes per status (§8 color coding): Scheduled=blue,
 * Completed=green, Cancelled=gray, NoShow=amber.
 */
export const INTERVIEW_STATUS_BADGE: Record<InterviewStatus, string> = {
  Scheduled: 'bg-blue-50 text-blue-700 ring-blue-200',
  Completed: 'bg-green-50 text-green-700 ring-green-200',
  Cancelled: 'bg-neutral-100 text-neutral-600 ring-neutral-200',
  NoShow: 'bg-amber-50 text-amber-700 ring-amber-200',
};

/** Tailwind badge classes per interview type (subtle, type-tinted). */
export const INTERVIEW_TYPE_BADGE: Record<InterviewType, string> = {
  InPerson: 'bg-violet-50 text-violet-700 ring-violet-200',
  Video: 'bg-sky-50 text-sky-700 ring-sky-200',
  Phone: 'bg-teal-50 text-teal-700 ring-teal-200',
};

/** Default interview duration in minutes (FR-1). */
export const DEFAULT_DURATION_MINUTES = 60;

/** Duration presets shown in the form select (minutes). */
export const DURATION_OPTIONS: readonly number[] = [15, 30, 45, 60, 90, 120];

// ─── Entities ─────────────────────────────────────────────────

/** One interviewer participant on an interview (employee reference). */
export interface IInterviewer {
  employeeId: string;
  /** Display name (backend joins from the employee record). */
  name: string;
  /** Department label for the avatar sublabel (§8), optional. */
  department?: string | null;
}

/**
 * A scheduled interview (FR-2: multiple rounds per applicant tracked separately).
 * Maps from the backend `InterviewDto`; reconcile field names in the service.
 */
export interface IInterview {
  id: string;
  applicantId: string;
  vacancyId: string;
  /** 1-based round number (FR-2 / AC-4). */
  roundNumber: number;
  interviewType: InterviewType;
  /** Interview date, ISO yyyy-MM-dd. */
  scheduledDate: string;
  /** Start time, 24h HH:mm. */
  startTime: string;
  durationMinutes: number;
  /** Required for in-person; null otherwise. */
  location: string | null;
  /** Required for video; null otherwise. */
  videoLink: string | null;
  /** Notes for interviewers (optional rich text / plain). */
  notes: string | null;
  status: InterviewStatus;
  interviewers: IInterviewer[];
  /** Applicant display name (for the tenant-wide agenda, FR-5), optional. */
  applicantName?: string | null;
  /** Vacancy title (for the tenant-wide agenda, FR-5), optional. */
  vacancyTitle?: string | null;
  createdAt?: string | null;
}

/**
 * Schedule / reschedule request (FR-1). `roundNumber` is omitted on create — the
 * backend assigns the next round; sent on reschedule to preserve it. Conditional
 * fields (`location`, `videoLink`) are validated client-side by interview type and
 * re-validated server-side (NFR-6).
 */
export interface IInterviewRequest {
  applicantId: string;
  vacancyId: string;
  interviewerIds: string[];
  interviewType: InterviewType;
  /** yyyy-MM-dd (future only — BR-3 / NFR-6). */
  scheduledDate: string;
  /** HH:mm 24h. */
  startTime: string;
  durationMinutes: number;
  location?: string | null;
  videoLink?: string | null;
  notes?: string | null;
}

/**
 * Result of a schedule / reschedule call. The operation SUCCEEDS even when an
 * interviewer is double-booked (FR-7) — the backend returns `warnings[]` for the
 * recruiter to acknowledge (soft conflict). Defaults to `[]` when absent.
 */
export interface IScheduleResult {
  interview: IInterview;
  warnings: string[];
}

/** Filters for the tenant-wide interviews agenda/calendar (FR-5 / FR-6). */
export interface IInterviewFilters {
  interviewerId?: string | null;
  vacancyId?: string | null;
  /** On-or-after date (yyyy-MM-dd). */
  from?: string | null;
  /** On-or-before date (yyyy-MM-dd). */
  to?: string | null;
  status?: InterviewStatus | null;
}

// ─── Helpers ──────────────────────────────────────────────────

/** Human label for an interview type, tolerating unknown values. */
export function interviewTypeLabel(type: InterviewType | string): string {
  return INTERVIEW_TYPE_LABELS[type as InterviewType] ?? String(type);
}

/** Human label for a status (NoShow → "No-show"), tolerating unknown values. */
export function interviewStatusLabel(status: InterviewStatus | string): string {
  return INTERVIEW_STATUS_LABELS[status as InterviewStatus] ?? String(status);
}

/** Badge classes for a status, falling back to the Scheduled style. */
export function interviewStatusBadge(status: InterviewStatus | string): string {
  return (
    INTERVIEW_STATUS_BADGE[status as InterviewStatus] ??
    INTERVIEW_STATUS_BADGE.Scheduled
  );
}

/** Badge classes for a type, falling back to the in-person style. */
export function interviewTypeBadge(type: InterviewType | string): string {
  return (
    INTERVIEW_TYPE_BADGE[type as InterviewType] ??
    INTERVIEW_TYPE_BADGE.InPerson
  );
}

/** Initials from a display name (avatar fallback). */
export function nameInitials(name: string): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) {
    return '';
  }
  const first = parts[0][0] ?? '';
  const last = parts.length > 1 ? parts[parts.length - 1][0] ?? '' : '';
  return `${first}${last}`.toUpperCase();
}

/**
 * True when `date` (yyyy-MM-dd) + `time` (HH:mm) is strictly in the past relative
 * to `now`. Backs the BR-3 / NFR-6 client validator. Treats an unparseable input
 * as NOT past (let the server decide) to avoid blocking on a malformed value.
 */
export function isPastDateTime(
  date: string | null | undefined,
  time: string | null | undefined,
  now: Date = new Date(),
): boolean {
  if (!date) {
    return false;
  }
  // Build a local Date from the date (+ optional time). Missing time = start of day.
  const [y, m, d] = date.split('-').map((n) => Number(n));
  if (!y || !m || !d) {
    return false;
  }
  const [hh, mm] = (time ?? '00:00').split(':').map((n) => Number(n));
  const when = new Date(
    y,
    m - 1,
    d,
    Number.isFinite(hh) ? hh : 0,
    Number.isFinite(mm) ? mm : 0,
    0,
    0,
  );
  if (Number.isNaN(when.getTime())) {
    return false;
  }
  return when.getTime() < now.getTime();
}

/** Today's date as yyyy-MM-dd (local) — the date input `min` (BR-3). */
export function todayIsoDate(now: Date = new Date()): string {
  const y = now.getFullYear();
  const m = `${now.getMonth() + 1}`.padStart(2, '0');
  const d = `${now.getDate()}`.padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/**
 * Compute the end time label "HH:mm" from a start time + duration (cards/agenda).
 * Returns '' for an unparseable start.
 */
export function endTimeLabel(startTime: string, durationMinutes: number): string {
  const [hh, mm] = (startTime ?? '').split(':').map((n) => Number(n));
  if (!Number.isFinite(hh) || !Number.isFinite(mm)) {
    return '';
  }
  const total = hh * 60 + mm + (durationMinutes || 0);
  const eh = Math.floor((total % 1440) / 60);
  const em = total % 60;
  return `${`${eh}`.padStart(2, '0')}:${`${em}`.padStart(2, '0')}`;
}

/**
 * Group interviews by their `scheduledDate` (yyyy-MM-dd), returning date-sorted
 * groups with each group's interviews sorted by start time. Backs the agenda view
 * (FR-5). Pure so it is unit-testable.
 */
export function groupByDate(
  interviews: IInterview[],
): { date: string; interviews: IInterview[] }[] {
  const map = new Map<string, IInterview[]>();
  for (const iv of interviews ?? []) {
    const key = iv.scheduledDate;
    const bucket = map.get(key);
    if (bucket) {
      bucket.push(iv);
    } else {
      map.set(key, [iv]);
    }
  }
  return [...map.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([date, list]) => ({
      date,
      interviews: list
        .slice()
        .sort((a, b) => a.startTime.localeCompare(b.startTime)),
    }));
}
