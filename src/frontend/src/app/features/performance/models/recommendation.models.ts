/**
 * US-PRF-010: Performance-Based Recommendations (promotion, bonus, increment, …)
 * models matching the (ASSUMED) backend API contract. HR-led, sensitive
 * compensation data (NFR-3/NFR-5) — restricted to HR with Performance.Publish.All
 * (the org-wide workspace) and managers with Performance.Read.Team (their direct
 * reports only, AC-5). EXTENDS the /performance feature (HR/manager-gated).
 *
 * The service layer (`RecommendationService`) is intentionally thin so a route/DTO
 * mismatch is a one-file fix once the backend lands (like US-PRF-001..009).
 *
 * ── ASSUMED backend contract (backend agent must confirm/reconcile) ────────────
 * `apiBaseUrl` already includes `/api/v1`. All under `/performance/recommendations`.
 * Tenant + acting user resolved server-side from the session (FE sends no tenant/user
 * id). The server enforces SCOPE + access (NFR-5/AC-5): `Performance.Publish.All` →
 * the org-wide HR workspace; `Performance.Read.Team` → the manager's direct reports
 * only ("Team Recommendations"). A caller with neither is 403 server-side (the FE also
 * hides the nav + redirects). Compensation fields are encrypted at rest (NFR-3) and
 * may be omitted entirely when the caller lacks compensation visibility (a
 * tenant-configurable role flag — §10); the FE renders "—" when a field is null.
 *
 *   GET   /performance/recommendations/cycles/completed
 *         → ICompletedCycleOption[] (BR-1: only cycles with published final ratings,
 *           past calibration if enabled — the cycle picker). Tolerates a `{ data }` page.
 *
 *   GET   /performance/recommendations/workspace
 *         ?cycleId=&type=&status=&search=&departmentId=&sort=&dir=&page=&pageSize=
 *         → IRecommendationWorkspace (AC-1: paginated employee rows with final score,
 *           current grade/band, tenure, compensation, manager flags (US-PRF-003), and
 *           the current recommendation fields; plus the budget tracker + which export
 *           formats the backend can produce). One call drives the workspace screen (NFR-4).
 *
 *   POST  /performance/recommendations/auto-generate  body IAutoGenerateRequest
 *         ?preview=true|false
 *         → IAutoGeneratePreview (AC-2/FR-2): applies the rating-threshold rules across
 *           the cycle population. preview=true returns the proposed changes WITHOUT
 *           persisting (BR-3); preview=false persists draft recommendations and returns
 *           the same shape with `applied: true`.
 *
 *   PUT   /performance/recommendations/{recommendationId}  body IUpdateRecommendationRequest
 *         → IRecommendationRow (§8 inline edit / FR-3 manual override). A manual override
 *           of an auto-generated value REQUIRES a justification (FR-3) — the server
 *           re-validates and 400s without one. Returns the updated row + recomputed budget.
 *
 *   POST  /performance/recommendations/{recommendationId}/submit  body { comment? }
 *         → IRecommendationRow (AC-3): routes the recommendation through the tenant
 *           approval workflow (FR-4); status → Submitted/PendingApproval.
 *
 *   POST  /performance/recommendations/{recommendationId}/decision
 *         body { decision: 'Approve'|'Reject', comment? } → IRecommendationRow
 *         (approver action; status → Approved/Rejected). Approver-gated server-side.
 *
 *   GET   /performance/recommendations/budget?cycleId=  → IBudgetTracker
 *         (FR-8/BR-4: allocated vs consumed; the workspace embeds this but it can be
 *           re-fetched after each edit/submit to refresh the tracker widget).
 *
 *   GET   /performance/recommendations/summary?cycleId=  → IRecommendationSummary
 *         (AC-4/FR-6: aggregate stats — total promotions, bonus pool allocated,
 *           increment distribution by department, comparison vs the previous cycle).
 *
 *   GET   /performance/recommendations/team?cycleId=  → IRecommendationRow[]
 *         (AC-5: a manager's DIRECT REPORTS only with final score + recommendation
 *           status. Distinct from the HR workspace. Tolerates a `{ data }` page.)
 *
 *   GET   /performance/recommendations/export?format=Excel|Pdf&cycleId=&…
 *         → HttpResponse<Blob> (FR-6/Content-Disposition filename). The workspace's
 *           `availableExportFormats` gates which buttons render.
 *
 * Bare payloads (US-PLT-001 unwrap); PascalCase enum strings (US-PLT-003), never ints.
 */

import type { Schema } from '@core/api';

// ─── Enums (PascalCase strings, US-PLT-003) ──────────────────────────────────

/** FR-1: the kinds of recommendation HR can make. */
export type RecommendationType =
  | 'None'
  | 'Promotion'
  | 'Bonus'
  | 'Increment'
  | 'TrainingNomination'
  | 'LateralMove'
  | 'PipReferral';

/** Lifecycle status of a single recommendation. */
export type RecommendationStatus =
  | 'Draft'
  | 'Submitted'
  | 'PendingApproval'
  | 'Approved'
  | 'Rejected';

/** Export formats the backend can produce (PascalCase, US-PLT-003). */
export type RecommendationExportFormat = 'Excel' | 'Pdf';

/** Budget health band — drives the tracker bar color (FR-8/BR-4). */
export type BudgetHealth = 'Healthy' | 'Warning' | 'Exceeded';

// ─── DTOs ────────────────────────────────────────────────────────────────────

/** A completed cycle eligible for recommendations (BR-1). */
export interface ICompletedCycleOption {
  cycleId: string;
  cycleName: string;
  /** ISO date the cycle's final ratings were published. */
  ratingsPublishedOn: string | null;
}

/**
 * The recommendation detail carried on each row (the editable part, §8 inline edit).
 * Amounts are nullable: a bonus/increment carries an amount; a promotion carries a
 * target grade + effective date (BR-5); a training nomination carries a course.
 */
export interface IRecommendationDetail {
  type: RecommendationType;
  /** Bonus / increment monetary amount (compensation PII; may be null). */
  amount: number | null;
  /** Increment percentage (alternative to a flat amount). */
  percentage: number | null;
  /** Promotion target grade/band (BR-5). */
  targetGrade: string | null;
  /** Promotion target job title. */
  targetTitle: string | null;
  /** Promotion / increment effective date (BR-5), ISO date. */
  effectiveDate: string | null;
  /** Training course for a TrainingNomination. */
  trainingCourse: string | null;
  /** Justification — MANDATORY on a manual override (FR-3). */
  justification: string | null;
}

/** One employee row in the HR workspace / a manager's team list (AC-1/AC-5). */
export interface IRecommendationRow {
  /** The recommendation record id (stable even when type is None/empty). */
  recommendationId: string;
  employeeId: string;
  employeeName: string;
  employeeNo: string;
  departmentId: string | null;
  departmentName: string | null;
  jobTitle: string | null;
  /** Final published performance score (AC-1). */
  finalScore: number | null;
  /** The scale finalScore is on (e.g. 5 or 100) — for the score badge. */
  scoreScaleMax: number;
  /** Current grade/band (AC-1). */
  currentGrade: string | null;
  /** Current annual compensation (compensation PII; null if not visible). */
  currentCompensation: number | null;
  /** Tenure in whole months (AC-1). */
  tenureMonths: number | null;
  /** Manager recommendation flag from US-PRF-003 (e.g. `Promotion`) — see `ReviewFlag`, GAP-012a. */
  managerFlag: string | null;
  status: RecommendationStatus;
  /** True when the current detail was produced by auto-generation (FR-3 override gate). */
  autoGenerated: boolean;
  detail: IRecommendationDetail;
}

/** A page of workspace rows (AC-1 pagination, NFR-4). */
export interface IRecommendationPage {
  rows: IRecommendationRow[];
  totalCount: number;
  page: number;
  pageSize: number;
}

/** FR-8/BR-4: the bonus/increment budget tracker. */
export interface IBudgetTracker {
  /** Whether budget tracking is enabled for the tenant/cycle (§10 optional). */
  enabled: boolean;
  /** Currency code for display (e.g. "USD"). */
  currency: string;
  allocated: number;
  consumed: number;
}

/** The whole workspace payload — one call drives the AC-1 screen. */
export interface IRecommendationWorkspace {
  cycleId: string;
  cycleName: string;
  page: IRecommendationPage;
  budget: IBudgetTracker;
  /** True when the caller may see compensation fields (§10 tenant-configurable). */
  compensationVisible: boolean;
  /** Which export formats the backend can produce now (FR-6 — PDF gated). */
  availableExportFormats: RecommendationExportFormat[];
}

/** One rating-threshold → recommendation-type rule (AC-2/FR-2 wizard). */
export interface IAutoGenerateRule {
  /** Inclusive minimum final score (on the cycle's scale) for this rule to apply. */
  minScore: number;
  type: RecommendationType;
  /** Default bonus/increment amount the rule seeds (optional). */
  amount: number | null;
  /** Default increment percentage the rule seeds (optional). */
  percentage: number | null;
}

/** The auto-generate request body (AC-2/FR-2). */
export interface IAutoGenerateRequest {
  cycleId: string;
  rules: IAutoGenerateRule[];
  /** When true, only employees WITHOUT an existing manual recommendation are touched. */
  skipManualOverrides: boolean;
}

/** One proposed change in the auto-generate preview (AC-2/BR-3). */
export interface IAutoGeneratePreviewRow {
  employeeId: string;
  employeeName: string;
  finalScore: number | null;
  /** The type the matched rule proposes (None when no rule matched). */
  proposedType: RecommendationType;
  amount: number | null;
}

/** The auto-generate preview/apply response (AC-2). */
export interface IAutoGeneratePreview {
  /** false for a preview (BR-3); true once persisted. */
  applied: boolean;
  rows: IAutoGeneratePreviewRow[];
  /** Count by proposed type, for the wizard summary. */
  affectedCount: number;
}

/** §8 inline edit / FR-3 manual override request. */
export interface IUpdateRecommendationRequest {
  type: RecommendationType;
  amount: number | null;
  percentage: number | null;
  targetGrade: string | null;
  targetTitle: string | null;
  effectiveDate: string | null;
  trainingCourse: string | null;
  justification: string | null;
}

/** AC-4 summary: increment/promotion distribution by department. */
export interface IDepartmentRecommendationStat {
  departmentId: string;
  departmentName: string;
  promotionCount: number;
  bonusCount: number;
  incrementCount: number;
  /** Total bonus + increment money allocated to this department. */
  allocatedAmount: number;
}

/** AC-4 summary: comparison of an aggregate vs the previous cycle. */
export interface ICycleComparisonStat {
  label: string;
  current: number;
  previous: number;
}

/** AC-4 aggregate summary (FR-6 — the leadership view). */
export interface IRecommendationSummary {
  cycleId: string;
  cycleName: string;
  currency: string;
  totalPromotions: number;
  totalIncrements: number;
  totalTrainingNominations: number;
  /** Total bonus pool money allocated (AC-4). */
  bonusPoolAllocated: number;
  byDepartment: IDepartmentRecommendationStat[];
  /** Comparison rows vs the previous cycle (AC-4). */
  comparison: ICycleComparisonStat[];
}

// ─── Wire contract → view-model mappers (US-PRF-010 D-perf slice 1) ───────────
//
// The workspace read is the GENERATED contract type, not a hand-written guess.
// `RecommendationWorkspaceWire` is `Schema<'PerformanceRecommendationWorkspaceDto'>`.
//
// **Why this was migrated.** `IRecommendationWorkspace` declared a nested
// `page: { rows, totalCount, page, pageSize }`. The wire is FLAT: `page` is an
// integer (the page NUMBER), and `rows`/`totalCount`/`pageSize` are its top-level
// siblings. The service read `ws.page.rows` → `(number).rows` → `undefined`, so
// `getTeamRecommendations()` returned `[]` unconditionally and the HR workspace table
// was permanently empty with a 0 count — silently, no error. Consuming the generated
// type makes that a compile error rather than a silent empty list.

export type RecommendationWorkspaceWire =
  Schema<'PerformanceRecommendationWorkspaceDto'>;
export type RecommendationWorkspaceRowWire =
  Schema<'PerformanceRecommendationWorkspaceRowDto'>;
export type RecommendationBudgetWire = Schema<'PerformanceRecommendationBudgetDto'>;

/**
 * Maps the wire budget onto the tracker view-model.
 *
 * NOTE (finding — see report): `IBudgetTracker.enabled` has **no dedicated wire
 * field** — the workspace budget DTO carries `allocatedAmount`/`consumedAmount`/
 * `currency`/`isExceeded`/`name` but no `isEnabled`. The template gates the whole
 * budget card on `budget()?.enabled`, so it cannot simply be dropped. Here `enabled`
 * is derived from the PRESENCE of the budget object (the backend includes a budget
 * only for a cycle that has one), which is the closest faithful reading; if the
 * backend later adds an explicit flag, this mapper is the single place to wire it.
 */
export function mapRecommendationBudget(
  w: RecommendationBudgetWire | undefined,
): IBudgetTracker {
  return {
    enabled: w != null,
    currency: w?.currency ?? '',
    allocated: w?.allocatedAmount ?? 0,
    consumed: w?.consumedAmount ?? 0,
  };
}

/**
 * Maps one workspace row onto the `IRecommendationRow` view-model. The editable
 * recommendation detail is nested under `recommendation` on the wire; the flat FE
 * `detail` collapses the type-specific bonus/increment amount+percent into a single
 * `amount`/`percentage` pair. `scoreScaleMax` is a WORKSPACE-level field
 * (`ratingScaleMax`), not a row field, so it is threaded in by the caller.
 */
export function mapRecommendationRow(
  w: RecommendationWorkspaceRowWire,
  scoreScaleMax: number,
): IRecommendationRow {
  const rec = w.recommendation;
  return {
    recommendationId: rec?.id ?? '',
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    employeeNo: w.employeeNo ?? '',
    departmentId: w.departmentId ?? null,
    departmentName: w.departmentName ?? null,
    jobTitle: w.currentTitle ?? null,
    finalScore: w.finalScore ?? null,
    scoreScaleMax,
    currentGrade: w.currentGrade ?? null,
    currentCompensation: w.currentCompensation ?? null,
    tenureMonths: w.tenureMonths ?? null,
    managerFlag: w.managerFlagName ?? null,
    status: (rec?.statusName ?? rec?.status ?? 'Draft') as RecommendationStatus,
    autoGenerated: rec?.isAutoGenerated ?? false,
    detail: {
      type: (rec?.typeName ?? rec?.type ?? 'None') as RecommendationType,
      amount: rec?.bonusAmount ?? rec?.incrementAmount ?? null,
      percentage: rec?.bonusPercent ?? rec?.incrementPercent ?? null,
      targetGrade: rec?.targetGrade ?? null,
      targetTitle: rec?.targetTitle ?? null,
      effectiveDate: rec?.effectiveDate ?? null,
      trainingCourse: rec?.trainingCourse ?? null,
      justification: rec?.justification ?? null,
    },
  };
}

/**
 * Maps the whole workspace wire payload onto the `IRecommendationWorkspace`
 * view-model — re-nesting the flat `rows`/`totalCount`/`page`/`pageSize` into the
 * `page` object the templates read, and threading `ratingScaleMax` into each row.
 */
export function mapRecommendationWorkspace(
  w: RecommendationWorkspaceWire,
): IRecommendationWorkspace {
  const scaleMax = w.ratingScaleMax ?? 0;
  return {
    cycleId: w.cycleId ?? '',
    cycleName: w.cycleName ?? '',
    page: {
      rows: (w.rows ?? []).map((r) => mapRecommendationRow(r, scaleMax)),
      totalCount: w.totalCount ?? 0,
      page: w.page ?? 1,
      pageSize: w.pageSize ?? 0,
    },
    budget: mapRecommendationBudget(w.budget),
    compensationVisible: w.compensationVisible ?? false,
    availableExportFormats: (w.availableExportFormats ??
      []) as RecommendationExportFormat[],
  };
}

// ─── pure presentation helpers (shared by components + asserted by specs) ─────

/** Human label for a recommendation type. */
export const RECOMMENDATION_TYPE_LABEL: Record<RecommendationType, string> = {
  None: 'No recommendation',
  Promotion: 'Promotion',
  Bonus: 'Bonus',
  Increment: 'Salary increment',
  TrainingNomination: 'Training nomination',
  LateralMove: 'Lateral move',
  PipReferral: 'PIP referral',
};

/** The recommendation types HR can pick in the inline editor / rules (excludes None as a rule target only via UI). */
export const RECOMMENDATION_TYPES: RecommendationType[] = [
  'None',
  'Promotion',
  'Bonus',
  'Increment',
  'TrainingNomination',
  'LateralMove',
  'PipReferral',
];

/** Tailwind badge classes for each lifecycle status (status chips, AC-3/AC-5). */
export const RECOMMENDATION_STATUS_BADGE: Record<RecommendationStatus, string> =
  {
    Draft: 'bg-neutral-100 text-neutral-600 ring-neutral-200',
    Submitted: 'bg-sky-50 text-sky-700 ring-sky-200',
    PendingApproval: 'bg-amber-50 text-amber-700 ring-amber-200',
    Approved: 'bg-emerald-50 text-emerald-700 ring-emerald-200',
    Rejected: 'bg-rose-50 text-rose-700 ring-rose-200',
  };

/** Human label for a lifecycle status chip. */
export const RECOMMENDATION_STATUS_LABEL: Record<RecommendationStatus, string> =
  {
    Draft: 'Draft',
    Submitted: 'Submitted',
    PendingApproval: 'Pending approval',
    Approved: 'Approved',
    Rejected: 'Rejected',
  };

/**
 * AC-2/FR-2 — pure rule engine used by the wizard PREVIEW. Picks the
 * highest-threshold rule whose `minScore` the employee's final score meets. Rules
 * are evaluated highest-threshold-first so the strongest matching reward wins; an
 * unrated employee (null score) matches nothing → 'None'. Deterministic + side-effect
 * free so the preview the FE shows equals what the backend will apply.
 */
export function matchAutoRule(
  finalScore: number | null,
  rules: IAutoGenerateRule[],
): IAutoGenerateRule | null {
  if (finalScore == null) {
    return null;
  }
  const sorted = [...rules].sort((a, b) => b.minScore - a.minScore);
  for (const rule of sorted) {
    if (finalScore >= rule.minScore) {
      return rule;
    }
  }
  return null;
}

/**
 * AC-2 — build the preview rows the wizard shows BEFORE applying (BR-3). Pure, so the
 * spec can assert the rule engine end-to-end without HTTP.
 */
export function buildAutoPreview(
  rows: readonly IRecommendationRow[],
  rules: IAutoGenerateRule[],
  skipManualOverrides: boolean,
): IAutoGeneratePreview {
  const previewRows: IAutoGeneratePreviewRow[] = [];
  for (const r of rows) {
    if (skipManualOverrides && !r.autoGenerated && r.detail.type !== 'None') {
      continue;
    }
    const match = matchAutoRule(r.finalScore, rules);
    previewRows.push({
      employeeId: r.employeeId,
      employeeName: r.employeeName,
      finalScore: r.finalScore,
      proposedType: match?.type ?? 'None',
      amount: match?.amount ?? null,
    });
  }
  const affectedCount = previewRows.filter(
    (p) => p.proposedType !== 'None',
  ).length;
  return { applied: false, rows: previewRows, affectedCount };
}

/**
 * FR-3 — the manual-override justification gate. A manual override (changing an
 * auto-generated row, or setting any non-None recommendation) REQUIRES a non-blank
 * justification. Returns true when the edit is allowed to be saved. Pure → asserted
 * by the spec and reused as the inline-editor submit gate.
 */
export function isOverrideJustified(
  request: IUpdateRecommendationRequest,
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  _wasAutoGenerated: boolean,
): boolean {
  // Clearing a recommendation entirely (type None) never needs a justification.
  if (request.type === 'None') {
    return true;
  }
  // Any non-None recommendation requires a non-blank justification (FR-3). The
  // `_wasAutoGenerated` flag is retained in the signature for the contract (an
  // override is always of an auto-generated value) but does not change the rule:
  // a manual recommendation always needs a justification.
  return (request.justification ?? '').trim().length > 0;
}

/** Budget consumed as a percent of allocated (0 when no budget). FR-8. */
export function budgetConsumedPercent(budget: IBudgetTracker): number {
  if (!budget.enabled || budget.allocated <= 0) {
    return 0;
  }
  return Math.max(0, Math.round((budget.consumed / budget.allocated) * 100));
}

/** Remaining budget (can go negative when exceeded — soft warning, BR-4). */
export function budgetRemaining(budget: IBudgetTracker): number {
  return budget.allocated - budget.consumed;
}

/**
 * FR-8/BR-4 — budget health from the consumed ratio. green < 80% (Healthy),
 * amber 80–100% (Warning), red > 100% (Exceeded — soft warning, never a hard block).
 * Pure → asserted by the budget-tracker threshold-color spec.
 */
export function budgetHealth(budget: IBudgetTracker): BudgetHealth {
  if (!budget.enabled || budget.allocated <= 0) {
    return 'Healthy';
  }
  const ratio = budget.consumed / budget.allocated;
  if (ratio > 1) {
    return 'Exceeded';
  }
  if (ratio >= 0.8) {
    return 'Warning';
  }
  return 'Healthy';
}

/** Tailwind bar-fill class for a budget health band (FR-8 green/amber/red). */
export const BUDGET_HEALTH_BAR: Record<BudgetHealth, string> = {
  Healthy: 'bg-emerald-500',
  Warning: 'bg-amber-500',
  Exceeded: 'bg-rose-500',
};

/** Tailwind text class for a budget health band. */
export const BUDGET_HEALTH_TEXT: Record<BudgetHealth, string> = {
  Healthy: 'text-emerald-600',
  Warning: 'text-amber-600',
  Exceeded: 'text-rose-600',
};

/** Soft-warning copy shown when the budget is exceeded (BR-4). QA may assert. */
export const BUDGET_EXCEEDED_MESSAGE =
  'Allocated budget exceeded — you can still proceed with justification.';

/** Tenure in whole months → a "Ny Nm" label (AC-1). null → "—". */
export function formatTenure(months: number | null): string {
  if (months == null || months < 0) {
    return '—';
  }
  const years = Math.floor(months / 12);
  const rem = months % 12;
  if (years === 0) {
    return `${rem}m`;
  }
  if (rem === 0) {
    return `${years}y`;
  }
  return `${years}y ${rem}m`;
}

/** Bar length percent for a department stat relative to the busiest department (AC-4). */
export function deptBarPercent(value: number, peak: number): number {
  if (peak <= 0) {
    return 0;
  }
  return Math.max(2, Math.round((value / peak) * 100));
}

/** Delta direction of a comparison stat vs the previous cycle (AC-4). */
export function comparisonDelta(stat: ICycleComparisonStat): number {
  return stat.current - stat.previous;
}

// ─── Residue wire mappers (US-PRF-010 D-perf slice 3) ─────────────────────────
// Slice 1 migrated the workspace read; these finish the service. All consume the
// GENERATED contract types.

export type CompletedCycleOptionWire =
  Schema<'PerformanceCompletedCycleOptionDto'>;
export type RecommendationDtoWire = Schema<'PerformanceRecommendationDto'>;
export type AutoGenerateResultWire = Schema<'PerformanceAutoGenerateResultDto'>;
export type RecommendationSummaryWire =
  Schema<'PerformanceRecommendationSummaryDto'>;

/** Field names match the wire 1:1; the mapper only supplies defaults for optionals. */
export function mapCompletedCycle(
  w: CompletedCycleOptionWire,
): ICompletedCycleOption {
  return {
    cycleId: w.cycleId ?? '',
    cycleName: w.cycleName ?? '',
    ratingsPublishedOn: w.ratingsPublishedOn ?? null,
  };
}

/**
 * Maps a single `PerformanceRecommendationDto` (the return of the inline-edit upsert,
 * submit, and approve/reject actions) onto `IRecommendationRow`. The workspace-level
 * `departmentId`/`departmentName`/`tenureMonths`/`scoreScaleMax` have NO field on the
 * single-recommendation DTO; they are defaulted here. This is safe because the
 * workspace's `next` handlers discard this value and call `reload()` — the mapped row
 * is never rendered (reported), so the mapper only needs to be type-faithful.
 */
export function mapRecommendationDto(
  w: RecommendationDtoWire,
): IRecommendationRow {
  return {
    recommendationId: w.id ?? '',
    employeeId: w.employeeId ?? '',
    employeeName: w.employeeName ?? '',
    employeeNo: w.employeeNo ?? '',
    departmentId: null,
    departmentName: null,
    jobTitle: w.currentTitle ?? null,
    finalScore: w.finalScore ?? null,
    scoreScaleMax: 0,
    currentGrade: w.currentGrade ?? null,
    currentCompensation: w.currentCompensation ?? null,
    tenureMonths: null,
    managerFlag: w.managerFlagName ?? w.managerFlag ?? null,
    status: (w.statusName ?? w.status ?? 'Draft') as RecommendationStatus,
    autoGenerated: w.isAutoGenerated ?? false,
    detail: {
      type: (w.typeName ?? w.type ?? 'None') as RecommendationType,
      amount: w.bonusAmount ?? w.incrementAmount ?? null,
      percentage: w.bonusPercent ?? w.incrementPercent ?? null,
      targetGrade: w.targetGrade ?? null,
      targetTitle: w.targetTitle ?? null,
      effectiveDate: w.effectiveDate ?? null,
      trainingCourse: w.trainingCourse ?? null,
      justification: w.justification ?? null,
    },
  };
}

/**
 * Maps the `PerformanceAutoGenerateResultDto` (the server auto-generate response) onto
 * `IAutoGeneratePreview`. NOTE: the workspace builds the PREVIEW client-side via
 * `buildAutoPreview` and discards this value on apply (reload), so it is not rendered.
 * `applied` has no dedicated wire flag — a positive created-count means it persisted.
 */
export function mapAutoGeneratePreview(
  w: AutoGenerateResultWire,
): IAutoGeneratePreview {
  const rows: IAutoGeneratePreviewRow[] = (w.suggestions ?? []).map((s) => ({
    employeeId: s.employeeId ?? '',
    employeeName: s.employeeName ?? '',
    finalScore: s.finalScore ?? null,
    proposedType: (s.typeName ?? s.type ?? 'None') as RecommendationType,
    amount: s.bonusAmount ?? s.incrementAmount ?? null,
  }));
  return {
    applied: (w.suggestionsCreated ?? 0) > 0,
    rows,
    affectedCount: w.suggestionsCreated ?? rows.length,
  };
}

/**
 * Maps `PerformanceRecommendationSummaryDto` onto `IRecommendationSummary`.
 * **Drift reconciled here** (the AC-4 summary crashed before this):
 *  • the wire has NO `byDepartment` — the per-department distribution is
 *    `incrementByDepartment` (increment-only); the un-migrated raw cast left
 *    `s.byDepartment` undefined and `recommendation-summary.component` hard-crashed on
 *    `s.byDepartment.length`;
 *  • the wire has NO `comparison` array — it is derived from `previousCycle`;
 *  • `IDepartmentRecommendationStat.promotionCount`/`bonusCount` have NO wire source
 *    (the wire only breaks INCREMENTS down by department) — defaulted 0 + reported;
 *  • `totalIncrements` (a COUNT) has no dedicated wire field — derived as the sum of
 *    the per-department increment recommendation counts + reported.
 */
export function mapRecommendationSummary(
  w: RecommendationSummaryWire,
): IRecommendationSummary {
  const byDepartment: IDepartmentRecommendationStat[] = (
    w.incrementByDepartment ?? []
  ).map((d) => ({
    departmentId: d.departmentId ?? '',
    departmentName: d.departmentName ?? '',
    promotionCount: 0,
    bonusCount: 0,
    incrementCount: d.recommendationCount ?? 0,
    allocatedAmount: d.totalIncrementAmount ?? 0,
  }));
  const prev = w.previousCycle;
  const comparison: ICycleComparisonStat[] = prev
    ? [
        {
          label: 'Promotions',
          current: w.totalPromotions ?? 0,
          previous: prev.totalPromotions ?? 0,
        },
        {
          label: 'Bonus pool',
          current: w.totalBonusPoolAllocated ?? 0,
          previous: prev.totalBonusPoolAllocated ?? 0,
        },
        {
          label: 'Increment allocated',
          current: w.totalIncrementAllocated ?? 0,
          previous: prev.totalIncrementAllocated ?? 0,
        },
      ]
    : [];
  return {
    cycleId: w.cycleId ?? '',
    cycleName: w.cycleName ?? '',
    currency: w.currency ?? '',
    totalPromotions: w.totalPromotions ?? 0,
    totalIncrements: byDepartment.reduce((n, d) => n + d.incrementCount, 0),
    totalTrainingNominations: w.totalTrainingNominations ?? 0,
    bonusPoolAllocated: w.totalBonusPoolAllocated ?? 0,
    byDepartment,
    comparison,
  };
}
