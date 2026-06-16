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

### Design choices
- Weight distribution bar is a **pure CSS/Tailwind stacked bar**, not chart.js
  (§8 suggested chart.js but no chart lib is a FE dependency — see frontend-dev
  memory `no-chart-lib-comparison-table`).
- Drag-reorder, cascade tree, and bulk template assignment (§8) were deferred as
  nice-to-haves; not implemented in US-PRF-001.
