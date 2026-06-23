# API-Layer QA Baseline — Notifications & Audit module

- **Date:** 2026-06-19
- **Environment:** Running backend at `http://localhost:5000`; tenant `acme` (subdomain header `X-Tenant-Subdomain: acme`).
- **Scope:** REST surface only (US-NTF-001 in-app notifications, US-NTF-002 templates, US-NTF-003 preferences). SignalR realtime push + actual email **dispatch** are known deferred seams and were **not** asserted.
- **Auth:** All four personas logged in successfully via `POST /api/v1/auth/login` (password `Admin@123!`, subdomain `acme`): Tenant Admin, HR Officer, Manager, Employee.
- **Method:** `curl` against the live API with persona bearer tokens.

## Route discovery (actual, vs task brief)

Controllers: `NotificationsController`, `NotificationTemplatesController`, `NotificationPreferencesController`.

| Area | Route base | AuthZ |
|------|-----------|-------|
| In-app notifications | `api/v1/notifications` | `[Authorize]` only — self-scoped, no permission gate |
| Preferences | `api/v1/notification-preferences` | `[Authorize]` only — self-scoped |
| Templates | `api/v1/notification-templates` | reads `Tenant.ViewSettings`, writes `Tenant.ManageSettings` |

**Brief deviations (not defects, just naming the task brief got wrong):**
- The template permission is **`Tenant.ViewSettings` / `Tenant.ManageSettings`**, *not* `Notifications.ManageTemplates`. Same gate as the company-settings console (held by Tenant Admin / Tenant Owner).
- Preferences categories are the `NotificationCategory` enum values: `LeaveUpdates, AttendanceAlerts, PayrollNotifications, OnboardingOffboarding, PerformanceReviews, RecruitmentUpdates, SystemAnnouncements, SecurityAlerts` (only `SecurityAlerts` is mandatory). The brief's `LeaveRequests` is not a real category.
- Template event keys: `leave_approved, onboarding_welcome, payslip_published, password_reset` (from `NotificationEventCatalog`).

## Results

| Endpoint / TC | Persona | Method | Verdict | HTTP | Evidence |
|---------------|---------|--------|---------|------|----------|
| `GET /notifications?page=1&pageSize=20` | Employee | GET | PASS | 200 | `{items:[],unreadCount:0,totalCount:0,page:1,pageSize:20}` — paged self-scoped list |
| `GET /notifications/unread-count` | Employee | GET | PASS | 200 | `{count:0}` (FR-5 bell badge) |
| `GET /notifications` | HR Officer | GET | PASS | 200 | own empty list |
| `GET /notifications` | Manager | GET | PASS | 200 | own empty list |
| `GET /notifications` | Tenant Admin | GET | PASS | 200 | own empty list (admin sees only own, not all-tenant) |
| `POST /notifications/read-all` | Employee | POST | PASS | 200 | `{updated:0}` (AC-5) |
| `POST /notifications/{randomGuid}/read` | Employee | POST | PASS | 404 | `Notification not found.` — not-owned/not-found → 404 (AC-4) |
| `POST /notifications/not-a-guid/read` | Employee | POST | PASS | 404 | malformed GUID → route no-match 404 (acceptable) |
| `GET /notifications` (bad/anon token) | anon | GET | PASS | 401 | unauthenticated rejected |
| `GET /notification-preferences` | Employee | GET | PASS | 200 | 8-category matrix + quiet hours (AC-1); `SecurityAlerts.isMandatory=true` |
| `PUT /notification-preferences/LeaveUpdates` | Employee | PUT | PASS | 200 | toggle persisted `{channelInApp:true,channelEmail:false}` (AC-2/3) |
| `PUT /notification-preferences/SecurityAlerts` both-off | Employee | PUT | PASS | 422 | `category_mandatory` — BR-2 enforced |
| `PUT /notification-preferences/LeaveUpdates` both-off | Employee | PUT | PASS | 422 | `both_channels_off` — BR-3 enforced |
| `PUT /notification-preferences/BogusCat` | Employee | PUT | PASS | 404 | `unknown_category` — unknown enum rejected |
| `PUT /notification-preferences/quiet-hours` | Employee | PUT | PASS | 200 | `{enabled:true,start:22:00:00,end:07:00:00,tz:Asia/Colombo}` (FR-9) |
| `PUT /notification-preferences/quiet-hours` bad time | Employee | PUT | PASS | 422 | `invalid_time` — `99:99` rejected |
| `POST /notification-preferences/reset` | Employee | POST | PASS | 200 | reverted matrix returned (FR-7) |
| `GET /notification-templates?language=en` | Tenant Admin | GET | PASS | 200 | catalog list, each `isCustom:false` (AC-1) |
| `GET /notification-templates/leave_approved` | Tenant Admin | GET | PASS | 200 | effective default subject+body+placeholders (AC-2) |
| `GET /notification-templates/bogus_event` | Tenant Admin | GET | PASS | 404 | `Unknown notification event` |
| `POST /notification-templates/leave_approved/preview` | Tenant Admin | POST | PASS | 200 | rendered with sample data: subject `Hi Jane`, body `Annual Leave` — placeholders resolved (FR-4) |
| `GET /notification-templates` | Employee | GET | PASS | 403 | non-admin blocked (`Tenant.ViewSettings` gate) |
| `GET /notification-templates` | Manager | GET | PASS | 403 | non-admin blocked |
| `GET /notification-templates` | HR Officer | GET | PASS | 403 | HR lacks `Tenant.ViewSettings` (settings-admin gate, not HR-domain) |
| `POST /notification-templates/leave_approved/preview` | Employee | POST | PASS | 403 | read-perm gate blocks preview |
| `PUT /notification-templates/leave_approved` | Employee | PUT | PASS | 403 | write blocked (`Tenant.ManageSettings`) |
| Cross-tenant: acme token + `X-Tenant: globex` | Employee | GET | PASS | 404 | resolution middleware → "workspace does not exist" before query runs |
| Missing tenant header | Employee | GET | PASS | 400 | `No tenant context.` — fail-closed |
| `X-Tenant: doesnotexist` | Employee | GET | PASS | 404 | non-existent workspace rejected |

## Findings

**No real defects found.** No 500s, no broken contracts, no auth gaps, no cross-user/cross-tenant notification leak observed on the REST surface.

1. **Tenant isolation holds (structurally + observed).** The notifications/preferences endpoints accept **no client-supplied userId** — the caller is always resolved from `ICurrentUser`, so a user cannot request another user's notifications by construction. Cross-tenant access is rejected at the resolution-middleware layer (404 "workspace not found" for a mismatched/unknown subdomain; 400 "No tenant context" when the header is absent), i.e. **fail-closed before** any tenant-scoped query executes. Read isolation additionally relies on the EF global query filter (`TenantId == _tenantContext.TenantId`) per the platform's documented Phase-3 model — note Postgres **RLS is deferred** (consistent with prior NTF/ADM notes), so isolation is app-layer, not DB-layer. No leak was demonstrable; a stronger seeded two-tenant + two-user data-leak test remains worthwhile once notification rows are seeded.

2. **Business-rule validations are wired and return the right codes.** Preferences enforce BR-2 (mandatory `SecurityAlerts` cannot be disabled → 422 `category_mandatory`) and BR-3 (cannot disable both channels → 422 `both_channels_off`), plus lenient time parsing with 422 `invalid_time` on bad quiet-hours input. Unknown category → 404 `unknown_category`.

3. **Template AuthZ is correct but coupled to the settings console.** Templates are gated by `Tenant.ViewSettings`/`Tenant.ManageSettings`, so **HR Officer and Manager are both 403** — only Tenant Admin/Owner can view or edit templates. This matches the controller's documented intent (US-NTF-002 AC-5) but differs from the task brief's assumed `Notifications.ManageTemplates` permission; flagging for the caller in case a dedicated template-manager role was intended.

4. **Empty data is a coverage limit, not a pass-by-default.** The acme tenant has zero seeded notifications, so list/read/mark-read happy paths returned empty/zero. The endpoints behaved correctly (200, correct shape, correct 404 on a random id), but **end-to-end "notification appears → mark read → unread-count decrements"** could not be exercised without a seeded or dispatched notification. Dispatch is the known deferred seam; recommend a seed fixture or a manual outbox insert to fully exercise US-NTF-001 read-state transitions.

5. **Deferred seams (not tested, by design):** SignalR realtime push and actual SMTP/email delivery for `test-email` were not invoked. The `test-email` endpoint (`POST /{eventKey}/test-email`, `Tenant.ManageSettings`) was intentionally not fired to avoid triggering the (likely no-op/sandbox) mail path; its AuthZ gate is the same `Tenant.ManageSettings` verified on the PUT write path.

## 6-line summary

- **Endpoints hit:** 12 distinct routes across notifications (list/unread-count/read/read-all), preferences (get/put-category/quiet-hours/reset), templates (list/get/preview + write/read AuthZ), plus 3 tenant-context negatives.
- **PASS:** 28/28 checks behaved per intent.
- **FAIL:** 0.
- **BLOCKED:** 0 (no missing dependency; empty seed data limited depth of US-NTF-001 read-state E2E but did not block the REST surface).
- **Real defects (status+route):** none — no 500s, no wrong-status, no contract breaks, no cross-user/cross-tenant notification leak.
- **Caller flags (non-defect):** template permission is `Tenant.ViewSettings`/`Tenant.ManageSettings` (not `Notifications.ManageTemplates`) → HR Officer & Manager are 403 on templates; Postgres RLS still deferred (isolation enforced at app/EF layer + resolution middleware); cannot E2E mark-read without seeded notifications (dispatch deferred).
