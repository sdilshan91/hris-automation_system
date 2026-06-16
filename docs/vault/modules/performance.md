---
type: module-note
module: performance
---

# Performance Management

Domain rules, edge cases, and FE↔BE contract notes for the Performance module.

## US-PRF-001 — Manager sets goals/KPIs for team members (FE established the module)

The frontend feature folder `src/frontend/src/app/features/performance/` was created
here. Lazy-loaded at `/performance` (roleGuard: Manager / HR Officer / HR Manager /
Tenant Admin). Two views: a team-goals dashboard (default) and a per-member
goal-setting form (`/performance/goals/:employeeId`).

### Goal validation rules (mirror the backend — keep both in sync)
- title ≤ 200 chars, description ≤ 2000 chars (FR-2).
- weight is a whole percent, **multiple of 5** (BR-3); per-employee weights must sum
  to **exactly 100%** (FR-3). The "Save Goals" button is gated on this; off-total
  shows the exact string **"Goal weights must total 100%"** (AC-3 — QA asserts it).
- 1-10 goals per employee per cycle (BR-2).
- category enum: `KPI` | `Competency` | `Project` (PascalCase strings, US-PLT-003).
- goal-setting status enum: `NotStarted` | `Draft` | `Submitted` | `Acknowledged`.

### FE↔BE contract the FE service ASSUMES (backend agent must confirm/reconcile)
`apiBaseUrl` already includes `/api/v1`. All under `/performance`:
- `GET  /cycles/active` → `IAppraisalCycle` incl. **`goalSettingOpen: boolean`**
  (authoritative window gate — the FE renders read-only + the closed message off
  this flag, AC-5; it does NOT compute open/closed from the dates client-side).
- `GET  /cycles/:cycleId/team` → `ITeamGoalStatus[]` (employeeId, employeeName,
  jobTitle, status, goalCount, totalWeight) — drives the dashboard (AC-4).
- `GET  /cycles/:cycleId/employees/:employeeId/goals` → `IGoal[]` (prefill).
- `PUT  /cycles/:cycleId/employees/:employeeId/goals` with `{ goals: IGoalInput[] }`
  → returns the persisted `IGoal[]`. This is a **full replace** of the goal set, not
  a per-goal CRUD. Server re-validates 100%/count and notifies the employee (FR-7).

If the backend lands per-goal CRUD instead of a bulk replace, the FE
`PerformanceGoalService` is the single-file change point.

### ⚠️ FE↔BE contract MISMATCH (US-PRF-001) — reconcile at US-PRF-004
The backend (authoritative, fully tested) actually shipped a DIFFERENT contract than the
FE service above assumes. They pass tests independently (mocked HTTP) but are NOT wired
end-to-end yet:

| Concern | FE assumes | BE (`GoalsController`) actually exposes |
|---|---|---|
| Base path | `/api/v1/performance` | `/api/v1/tenant/performance` |
| Active cycle | `GET /cycles/active` → `goalSettingOpen` flag | **no endpoint** (belongs to US-PRF-004) |
| Team dashboard | `GET /cycles/:id/team` | `GET /cycles/{id}/team-dashboard` |
| Employee goals | `GET /cycles/:id/employees/:eid/goals` | `GET /employees/{eid}/cycles/{id}/goals` |
| Save | `PUT …/goals` **full-replace** of `{goals:[]}` | per-goal `POST/PUT/DELETE /goals[/{id}]` |

Reconciliation is genuinely **blocked on US-PRF-004**: the FE renders the window gate off
an active-cycle endpoint that only exists once HR cycle-management lands. When US-PRF-004
is built, align `PerformanceGoalService` to the real routes (single-file change) and decide
full-replace vs per-goal CRUD (BE currently per-goal). Tracked in the US-PRF-001 PR.

## US-PRF-002 — Employee self-rates against goals ("My Review")

FE-only so far (backend not yet built). New employee-persona view, **separate top-level
route `/my-review`** (guard `['Employee','Manager','HR Officer','Tenant Admin']`) — NOT
under `/performance`, because `/performance` is gated to managers/HR. Mirrors the
`/my-payslips` self-service pattern (US-PAY-005). Files: `models/self-assessment.models.ts`,
`services/self-assessment.service.ts`, `components/my-review/`, `my-review.routes.ts`.

### FE↔BE contract the FE service ASSUMES (backend agent must build/reconcile)
`apiBaseUrl` includes `/api/v1`. All under `/performance/self-assessment`. Tenant +
employee resolved server-side from session (FE sends no ids); `Performance.Read.Self` + RLS.

- `GET  /performance/self-assessment/active` → `ISelfAssessment` — the whole "My Review"
  screen in one call: the active cycle, assigned goals (read-only goal fields + the
  employee's saved rating/achievement/comment/attachments), `ratingScaleMax`
  (tenant-configured scale, FR-2), and **`windowOpen: boolean`** (authoritative
  open/closed gate, AC-4 — FE renders read-only off this flag, NOT off dates).
- `PUT  /performance/self-assessment/{id}/draft` body `{goals:[{goalId,selfRating,
  achievementPercent,comment}]}` → `ISelfAssessment` (partial save, status stays Draft).
- `POST /performance/self-assessment/{id}/submit` same body → `ISelfAssessment`. Server
  re-validates all-goals-rated + each comment ≥20 chars, computes weighted self-score
  (FR-4), flips status→`Submitted`, locks, notifies the manager.
- `POST /performance/self-assessment/{id}/goals/{goalId}/attachments` multipart field
  **`file`** → `IAssessmentAttachment` (FR-5: ≤5 files, ≤10MB each; virus-scan + tenant
  storage). **Most speculative part of the contract** — the upload route/field is a guess.
- `DELETE /performance/self-assessment/{id}/attachments/{attachmentId}` → 204.

Status enum `SelfAssessmentStatus`: `NotStarted | Draft | Submitted` (PascalCase strings).
Closed-window message is the literal **"The self-assessment period for this cycle has ended"**
(AC-4 — QA asserts verbatim, exported as `WINDOW_CLOSED_MESSAGE`). Like US-PRF-001 this is a
thin single-file service so a route mismatch is a one-file fix; reconcile alongside US-PRF-004.

Deferred (AC-5 / FR-7 Hangfire deadline reminders) is a BACKEND concern — no FE work.
Rich-text comment is a plain textarea, drag-drop upload is a plain file input (§8 pragmatic).

### Design choices
- Weight distribution bar is a **pure CSS/Tailwind stacked bar**, not chart.js
  (§8 suggested chart.js but no chart lib is a FE dependency — see frontend-dev
  memory `no-chart-lib-comparison-table`).
- Drag-reorder, cascade tree, and bulk template assignment (§8) were deferred as
  nice-to-haves; not implemented in US-PRF-001.
