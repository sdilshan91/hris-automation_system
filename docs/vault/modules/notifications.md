---
type: module-note
module: Notifications & Audit
---

# Notifications & Audit

Domain rules, decisions, and gotchas for the Notifications module. See also [[backend-dev]].

## US-NTF-001 — In-App Notification System (real-time via SignalR)

### What was built (backend)
- **Entity** `Notification : BaseEntity` (so `TenantId` is auto-stamped + global-query-filter isolated).
  Fields: `UserId`, `Type`, `Title`, `Message`, `ResourceType?`, `ResourceId?`, `IsRead`, `ReadAt?`
  (BaseEntity supplies Id/TenantId/CreatedAt/audit — not duplicated). Table `notification`, snake_case.
  Two indexes: `(tenant,user,created_at)` for the panel read; partial `(tenant,user) WHERE is_read=false`
  for the badge count.
- **Two seams, deliberately split:**
  - `INotificationService` (Application/Common/Interfaces) — the **persist-then-push dispatcher** other
    modules call to RAISE a notification. Implemented by `SignalRNotificationService` in **HRM.Api**
    (lives there because it needs `IHubContext<NotificationHub>`). Takes `tenantId` EXPLICITLY so it works
    from background jobs / the onboarding outbox worker (no resolved `ITenantContext`). Opens its own DI
    scope per call. The SignalR push is best-effort — a push failure never undoes the committed row (BR-4).
  - `INotificationReadService` (impl `NotificationReadService` in Infrastructure) — the **read + mark-read**
    side for the bell/panel. Runs in the request scope; scoped to `ICurrentUser.UserId` within the tenant.
- **SignalR hub** `NotificationHub` at `/hubs/notifications`. JWT via query string `?access_token=`
  (wired in `Program.cs` `JwtBearerEvents.OnMessageReceived`, gated to that path only). On connect joins
  `t:{tenantId}:user:{userId}` + one `t:{tenantId}:role:{role}` per role. **Tenant id is taken from the
  resolved `ITenantContext` / JWT `tenant_id` claim, NEVER from client input, and the hub exposes no
  client-callable join method** — that is how BR-5 (reject cross-tenant group names) is enforced.
- Server pushes the client method **`ReceiveNotification`** with a single `NotificationDto` payload.
- MediatR: `GetNotificationsQuery` (paged 20/page, most-recent-first, items+unreadCount+totalCount+page+
  pageSize), `GetUnreadCountQuery`, `MarkNotificationReadCommand` (owner-only, 404 otherwise),
  `MarkAllNotificationsReadCommand` (returns `updated` count).
- Routes (all under `[Authorize]`, no extra permission gate — a user always owns their own notifications):
  - `GET  /api/v1/notifications?page&pageSize`
  - `GET  /api/v1/notifications/unread-count`
  - `POST /api/v1/notifications/{id}/read`
  - `POST /api/v1/notifications/read-all`

### Key decisions / gotchas
- **Redis backplane is OPTIONAL.** `AddStackExchangeRedis` is only added when a Redis connection string is
  configured; otherwise the default in-memory backplane is used. The committed `appsettings*.json` Redis
  string uses **`abortConnect=false,connectTimeout=1000`**, so StackExchange.Redis connects lazily and the
  app starts even with Redis down. Do NOT make Redis a hard startup dependency.
- **Existing `INotificationDispatcher` seam was left untouched.** Onboarding/Performance modules dispatch
  through `INotificationDispatcher` (log-only `LoggingNotificationDispatcher`). US-NTF-001 added the NEW
  `INotificationService` rather than rewiring that seam, to stay surgical. **Follow-up worth doing:** point
  `LoggingNotificationDispatcher.SendInAppAsync` at `INotificationService.CreateAndDispatchAsync` (and the
  onboarding outbox worker) so those modules deliver for real — that closes the many "TODO US-NTF" stubs
  scattered across Payroll/Performance/Admin (welcome email, payslip email, etc. are EMAIL, separate US-NTF-002).
- **Mark-read ownership** is enforced in the EF predicate (`Id == id && UserId == caller`) so a non-owned /
  cross-tenant / unknown id all collapse to a single 404 (no existence leak) — AC-4.
- `IClientProxy.SendAsync` is an **extension method**; NSubstitute can't intercept it. Tests stub/verify
  `SendCoreAsync` instead.

### Deferred (not built here)
- BR-2 (archive >90d to cold storage) + BR-3 (purge >1000/user) Hangfire jobs — not built.
- NFR-5 polling fallback + reconnection are FE concerns.
- Email channel = US-NTF-002.

## US-NTF-002 — Email Notification Templates per Tenant

### The tenant_id-null problem and how it was resolved (KEY decision)
The story models a "system default" template as a row with NULL `tenant_id`. That fights the platform's tenancy
machinery: `BaseEntity` auto-stamps `TenantId` (TenantInterceptor) and the global query filter forces
`TenantId == current` — so a NULL-tenant default would be HIDDEN from every resolved tenant. **Resolution: two
separate tables.**
- `SystemNotificationTemplate` — a PLAIN class (NOT `BaseEntity`, no `TenantId` column at all), platform-level,
  **no global query filter** (same pattern as `subscription_plans` / `PlanLimitOverride`). One row per
  (event_key, language). Seeded by DbInitializer. This is the readable-from-anywhere baseline.
- `NotificationTemplate : BaseEntity` — the tenant OVERRIDE table, fully tenant-scoped (global query filter kept
  intact, AC-5). Partial unique index `(tenant_id, event_key, language) WHERE is_deleted = false`.
This keeps the tenant filter on overrides untouched AND lets resolution fall back to the shared defaults.

### Event catalog (the registry)
`HRM.Domain.Notifications.NotificationEventCatalog` — pure/static. One `NotificationEventDefinition` per event:
EventKey, EventName, Placeholders[] (FR-3 reference panel), SampleData dict (FR-4 live preview), and the seeded
DefaultSubject/Html/Text. Seeded events: `leave_approved`, `onboarding_welcome`, `payslip_published`,
`password_reset`. **This is the single source of truth** for AC-1's list, AC-2's variable panel/preview, AND the
DbInitializer seed. Add a new email event HERE and it flows through everything; DbInitializer reseeds it on next start.

### Three seams (deliberately split)
1. `IEmailSender` (NEW generic send seam) + `LogOnlyEmailSender` default. **There was NO generic email abstraction
   before** — only module-specific log-only seams (`IPayslipEmailSender`, `ITenantWelcomeEmailService`), all
   marked `TODO(US-NTF)`. This story adds the first generic one. I did **NOT** rewire the existing module seams
   onto it (surgical) — that's follow-up. Log-only, never throws, no SMTP required to start/test.
2. `IEmailTemplateService` (impl `EmailTemplateService`) — the RESOLVE + RENDER seam **other modules call** to turn
   an event+data into a ready email. Resolve precedence: active tenant override → system default (requested lang)
   → system default ("en", BR-2/FR-6) → catalog's compiled default (covers an unseeded DB; a known event NEVER
   returns null/404). Render delegates to the pure `TemplateRenderer`.
3. `INotificationTemplateService` (impl `NotificationTemplateService`) — the tenant-admin CRUD/preview/test-email
   facade behind the controller.

### Rendering (BR-5)
`HRM.Domain.Notifications.TemplateRenderer` — pure `{{dotted.path}}` substitution over a nested
`Dictionary<string,object?>`. **Unresolved = empty string** (unknown leaf, null value, OR a path that walks into a
non-dictionary, OR a path that stops on a dictionary). Never emits raw `{{...}}`. Identical in preview and at send.

### Authz / routes
Gated with the **existing** `Tenant.ViewSettings` (reads) / `Tenant.ManageSettings` (writes) permissions — same
gate as the company-settings console (US-ADM-006); both are held by Tenant Admin + Tenant Owner. No new permission.
Routes under `/api/v1/notification-templates` (GET list, GET/{eventKey}, PUT/{eventKey}, DELETE/{eventKey}=reset,
POST/{eventKey}/preview, POST/{eventKey}/test-email). language is a `?language=` query param (default "en").

### Audit
Relies on the existing `AuditInterceptor` (FR-9/NFR-6) — override saves/resets are `BaseEntity` writes that get
stamped. Did NOT build a new audit subsystem (US-NTF-004 owns the structured audit trail).

### Deferred (not built here)
- FR-7 custom sender domain + SPF/DKIM verification (BR-4) — sender always the platform default; `FromAddress` is
  carried on `EmailMessage` but unused by the log-only sender.
- BR-6 max-2-language-variants-per-plan cap — not enforced (no per-event language cap yet; the model supports any
  language, only "en" defaults are seeded).
- Real SMTP/transactional delivery — log-only (TODO US-NTF, same as every other email seam).
- Outbox-pattern send in a Hangfire worker (§10) — test-email sends inline; event emails render via the seam,
  their dispatch wiring into Leave/Onboarding/Payroll is follow-up.
- Rewiring the existing module-specific email seams onto the new `IEmailSender` — left untouched (surgical).

## US-NTF-003 — Notification Preferences per User

### What was built (backend)
- **Entity** `NotificationPreference : BaseEntity` (tenant-stamped + global-query-filtered). A row exists ONLY when
  a user has customized a category — there is no pre-seeded full matrix. Fields: `UserId`, `Category`
  (enum-as-string), `ChannelInApp`, `ChannelEmail`, `IsMandatory`, `QuietHoursStart/End` (postgres `time`),
  `QuietHoursTimezone`. Partial unique index `(tenant_id, user_id, category) WHERE is_deleted=false`. Table
  `notification_preference`. Migration `AddNotificationPreferences` (generated, not hand-written).
- **Enum** `NotificationCategory` (8 values from FR-2), `HasConversion<string>()` + global JsonStringEnumConverter.
- **Defaults catalog** `HRM.Domain.Notifications.NotificationPreferenceDefaults` — pure/static, the single source of
  truth for the category list, display names, descriptions, default channel state, and the mandatory flag. This is
  the bottom of the FR-5 cascade (defaults < user overrides). **Only `SecurityAlerts` is mandatory** (both channels
  forced on, can't be disabled). Per-tenant Tenant-Admin configuration of mandatory categories (FR-4/AC-4) is
  DEFERRED — this static set IS the tenant-default baseline.
- **Dispatch seam** `INotificationPreferenceService` (impl in Infrastructure). Three concerns in one service:
  matrix CRUD (request scope, scoped to `ICurrentUser`) + `ShouldDeliverAsync(tenantId, userId, category, channel)`
  which takes tenant+user EXPLICITLY so it works from background jobs / the outbox (mirrors `INotificationService`).
  - **Return shape** `DeliveryDecision { Kind: Deliver|Suppressed|DeferredUntilQuietHoursEnd, DeferUntilUtc }`.
    Mandatory → always Deliver (ignores toggles AND quiet hours). Channel off → Suppressed. Email + quiet hours
    active in the user's tz → Defer with the next-window-end UTC instant (BR-5); in-app is NEVER deferred.
  - Quiet-hours math handles overnight windows (end ≤ start wraps midnight) via `TimeZoneInfo` + a `TimeProvider`
    (inject a fake for deterministic tests — no Microsoft.Extensions.TimeProvider.Testing dep in the test project,
    so a tiny `FakeTimeProvider` lives in the test file).
- **BR enforcement at the command layer** (in the service, returns `Result` 422 — needs the category's mandatory
  state so it can't be a pure validator): BR-2 mandatory-can't-be-disabled (`category_mandatory`), BR-3
  non-mandatory-can't-have-both-channels-off (`both_channels_off`).
- **Quiet hours are a per-USER setting** stored redundantly on every one of that user's preference rows (read from
  any row). `UpdateQuietHours` writes the window to all the user's rows; if the user has no rows yet it creates a
  single carrier row on the first non-mandatory category (keeping that category's default channel state). IANA tz
  validated via `TimeZoneInfo.FindSystemTimeZoneById` in the service.
- **Reset (FR-7)** HARD-deletes the user's override rows (RemoveRange) → matrix falls back to pure defaults.

### Key decisions / gotchas
- **Redis is OPTIONAL** (NFR-3) — reused the SAME `IDistributedCache?` optional-ctor-param pattern as
  `TenantSettingsService` (memory cache always registered as fallback in DI, real Redis only when configured). The
  dispatch lookup caches a JSON snapshot under `t:{tenantId}:u:{userId}:notif-prefs` (TTL 5 min), falls back to a
  live DB read on miss/any cache error, and is invalidated on every preference write. Cache is never a hard dep.
- **Controller never accepts a userId** — every endpoint operates on `ICurrentUser` only. `{category}` is a string
  route param parsed to the enum (unknown → 404). Quiet-hours times come in as STRINGS ("HH:mm" or "HH:mm:ss")
  parsed leniently in the controller, because System.Text.Json's `TimeOnly` binder only accepts "HH:mm:ss".
- Routes (all `[Authorize]`, no extra permission gate — a user owns their own prefs):
  - `GET  /api/v1/notification-preferences` → `ApiResponse<PreferenceMatrixDto>`
  - `PUT  /api/v1/notification-preferences/{category}` body `{channelInApp, channelEmail}` → `CategoryPreferenceDto` (422 on BR-2/BR-3)
  - `PUT  /api/v1/notification-preferences/quiet-hours` body `{enabled, start, end, timezone}` → `QuietHoursDto`
  - `POST /api/v1/notification-preferences/reset` → `ApiResponse<PreferenceMatrixDto>`
- **The dispatcher does NOT yet call `ShouldDeliverAsync`.** This story builds the seam + the matrix; wiring it into
  `INotificationService.CreateAndDispatchAsync` (and the email send path / outbox to honor the DEFER signal) is
  follow-up — same "seam built, invocation deferred" pattern as US-NTF-002. The deferred-email QUEUE itself
  (a Hangfire-scheduled send at `DeferUntilUtc`) is not built.

### Deferred (not built here)
- FR-4 Tenant-Admin mandatory-category configuration (Admin Console) — static catalog is the baseline.
- Wiring `ShouldDeliverAsync` into the live dispatch path + the deferred-email queue/worker (BR-5 send-after).
- SMS channel (Phase 2 — model accommodates a 3rd channel but it's not added).
- PostgreSQL RLS (platform-wide deferred US-PLT-002) — isolation is the EF global query filter + TenantInterceptor.

## US-NTF-004 — Audit Trail for All Data Changes (automatic generic capture)

### The gap this filled (and what it did NOT duplicate)
Substantial audit infra already existed: the SINGLE `AuditLog` table (extended additively by US-PAY-012 with
Action/ResourceType/ResourceId/Before/After/ActorEmployeeNo/TraceId + impersonation cols), `IAuditLogService`
(US-ADM-008 READ+EXPORT, append-only by convention), `EmployeeFieldAuditLog` (Core HR field/PII audit), and
many EXPLICIT writers (AuthService, PayrollAuditLogger, Admin/Recruitment services). **The one missing piece was
AUTOMATIC capture of generic INSERT/UPDATE/DELETE with before/after diffs.** That is all this story added — no
second audit table, no duplicate service.

### What was built (backend)
- **`IAuditableEntity`** — empty OPT-IN marker in `HRM.Domain.Entities`. The capture interceptor records ONLY
  entities implementing it. Opt-IN (not "every BaseEntity") to avoid noise AND, critically, to avoid DOUBLE
  audit rows on entities whose service already writes its own audit trail.
- **`AuditCaptureInterceptor : SaveChangesInterceptor`** (Infrastructure/Persistence/Interceptors) — SEPARATE
  from the existing `AuditInterceptor` (which only stamps BaseEntity audit fields + impersonation attribution).
  Captures in `SavingChanges(Async)` and adds `AuditLog` rows to the SAME context so they commit in the SAME
  save (no second SaveChanges; insert resource ids are already UUIDv7-stamped by AuditInterceptor, which runs
  first). Action = "{EntityName}.{Create|Update|Delete}". BR-2: Before null for INSERT, After null for DELETE.
  AC-3: soft-delete (IsDeleted false→true) → "{Entity}.Delete" with before/after on the flag. BR-3: UPDATE
  captures ONLY changed props (value-diff, robust across providers; skips CreatedAt/CreatedBy/UpdatedAt/
  UpdatedBy noise). FR-7/FR-8 enrichment: TenantId from ITenantContext, actor from ICurrentUser, IP/UA from
  IHttpContextAccessor, TraceId from `Activity.Current?.Id ?? http.TraceIdentifier` — same pattern as
  PayrollAuditLogger. **Recursion is impossible**: AuditLog/EmployeeFieldAuditLog don't implement the marker.
  Registered LAST in `options.AddInterceptors(tenant, audit, capture)` — order matters so stamped TenantId +
  generated Id are visible at capture time.
- **`IAuditAnonymizationService` / `AuditAnonymizationService`** (BR-6 GDPR right-to-be-forgotten) — anonymizes
  a user's audit PII IN PLACE (the one sanctioned mutation of the append-only table): IP/UA/ActorEmployeeNo →
  `REDACTED-{userId}`, and scrubs the user's id/email out of Before/After/Detail JSON while preserving row
  count + structure (BR-1). Runs cross-tenant via `IgnoreQueryFilters`. **Idempotent** — Scrub masks existing
  tokens behind a sentinel before replacing the id, else the guid embedded inside `REDACTED-{guid}` would be
  re-redacted into `REDACTED-REDACTED-{guid}`.

### Entities marked IAuditableEntity (and why each is safe)
- **Department**, **JobTitle**, **LeaveRequest** — verified their services (`DepartmentService`/`JobTitleService`/
  `LeaveRequestService`) write NO explicit `AuditLog`/`new AuditLog` rows (grep-confirmed), so auto-capture adds
  no double rows. AC-3 explicitly references LeaveRequest soft-delete.
- **Employee was deliberately NOT marked** — `EmployeeService` already writes `EmployeeFieldAuditLog` (field/PII
  audit, US-CHR-002) with tests asserting that behavior; auto-capturing generic Employee rows risks
  double/conflicting audit. Marking is incrementally opt-in-able later if EmployeeFieldAuditLog is reconciled.
- The marker is additive: adding it to more entities later is a one-line change with no migration.

### Coverage VERIFIED (not rebuilt)
- **FR-3/AC-4 auth events** are already comprehensively logged by `AuthService.WriteAuditLog(WithDetail)Async`:
  login_failure, account_locked/unlocked, all mfa_* (enroll/challenge/disabled/recovery), session
  expired/revoked/concurrent-denied, tenant_switch, etc. NOT rebuilt — left untouched (surgical; large test
  suite). Gap note: explicit "login_success"/"logout"/"password_change" named events aren't emitted, but
  failures + the full security-event set ARE — not worth the regression risk to add here.
- **FR-4/AC-2 PII-read audit** ("Employee.ReadSensitive") — Core HR concern; not part of this generic-capture
  gap. Not added here.
- **NFR-3 BRIN index / NFR-6 monthly partitioning** — `AddAuditLogRetentionAndIndexes` already added
  tenant/action/resource/user composite indexes. BRIN + partitioning are pure-DB infra, DEFERRED.

### Tests (14, all green; full suite 2495 pass, 0 regressions)
`AuditCaptureInterceptorTests` (insert→after-only, update→only-changed-props, soft-delete→Delete, non-auditable
NOT captured, no recursion, tenant+actor+IP+UA+trace enrichment) + `AuditAnonymizationServiceTests` (redact in
place / preserve count, other users untouched, idempotent, empty-user no-op). **Gotcha:** these tests wire ONLY
the capture interceptor (not TenantInterceptor), so seeded entities must carry `TenantId` explicitly or the
Department/LeaveRequest `!IsDeleted && TenantId==` query filter hides them on re-read. Existing unit tests use
`TestDbContextFactory` which wires NO interceptors, so marking entities can't break their assertions.

### Deferred (not built here)
- PostgreSQL RLS + DB-role append-only UPDATE/DELETE revocation (FR-6/AC-5/NFR-2) — platform infra, consistent
  with US-PLT-002 RLS being inert. Append-only is code convention today.
- Monthly partitioning (NFR-6) + BRIN index (NFR-3) — DB infra.
- FR-9 streaming export to ELK/Splunk; NFR-5 async/outbox high-throughput capture (writes are synchronous).
- FR-4 PII-read auditing; FR-3 login_success/logout/password_change named events.

## US-NTF-005 — Audit Log Viewer with Filters (mostly reused US-ADM-008)

### What already existed and was REUSED (NOT rebuilt)
US-ADM-008 already shipped the whole base viewer under `/api/v1/tenant/audit-logs`: `AuditLogController`
(List + Get + GET/POST Export, gated `Audit.View`/`Audit.Export`), `AuditLogService` (explicit
`ITenantContext` scope — `audit_logs` has NO global query filter), `AuditLogFilter`/`AuditLogPageDto`
(`TotalCount`+`RetentionDays`)/`AuditLogDetailDto` (masked before/after + UA + traceId), CSV/JSON
`AuditLogExporter`, `SensitiveFieldMasker`, the BR-4 "AuditLog.Export" self-audit, and the retention purge.
US-NTF-005 added ONLY genuine deltas, all additive — no shape changes to existing DTOs.

### Deltas built
- **Meta-audit on view (FR-9/BR-5):** `ListAsync` now writes ONE `Action="AuditLog.View"` row per LIST request
  (NOT on Get — verified by a test) via a new private `WriteViewAuditAsync` (actor + tenant + IP/UA/trace from an
  OPTIONAL `IHttpContextAccessor` ctor param, same pattern as `PayrollAuditLogger`). It's a plain insert — never
  goes through ListAsync, so it cannot recurse. Best-effort: wrapped in try/catch + LogWarning so a meta-audit
  write failure never fails the user's read.
- **KEY decision — meta-audit rows are EXCLUDED from the default list/export.** `BuildFilteredQuery` drops
  `Action=="AuditLog.View"` rows UNLESS the caller explicitly filters for that action. Rationale: a viewer that
  lists its own view-events is self-referential noise (every page load inflates the next load's count) AND it
  keeps US-ADM-008's exact-count test assertions intact (they never request "AuditLog.View"). The rows are still
  persisted, tenant-scoped, and forensically queryable when explicitly asked for. This was the fix for the 2
  US-ADM-008 tests that broke when the view-row first started polluting the shared in-memory DB.
- **Multi-select filters (FR-2):** `AuditLogFilter` gained optional `Actions`/`ResourceTypes` arrays (positional
  params with `= null` defaults so the 6-arg `EmptyFilter()` + handlers still compile). Controller `List` gained
  repeatable `actions`/`resourceTypes` query params. `CombineValues(single, many)` folds the back-compat singular
  value into the multi-select group → OR within a group, AND across groups. Singular `action`/`resourceType`
  unchanged.
- **Actor autocomplete (FR-2):** `GET /api/v1/tenant/audit-logs/actors?search={q}&limit=` (gated `Audit.View`) →
  distinct actors (userId+name+email) appearing in THIS tenant's audit log, name/email type-ahead, capped 20.
  Distinct actor ids from `audit_logs` (explicit tenant scope) then resolved against the GLOBAL users table.
- **Filter options (FR-2, optional):** `GET /api/v1/tenant/audit-logs/filter-options` (gated `Audit.View`) →
  distinct `actions` + `resourceTypes` for the tenant, to populate the dropdowns.
- **Keyword across before/after (FR-2):** ALREADY covered by US-ADM-008's `SearchQuery` (matches Before/After/
  Detail case-sensitive contains). Left as-is; added a test asserting before AND after both match.

### Deferred (documented, consistent with US-ADM-008's own deferrals)
- Keyset/cursor pagination (FR-6) — kept OFFSET page/pageSize; a perf-only refactor on a dev DB is pure
  regression risk.
- Async Hangfire export + signed-URL object storage + 15-min expiry (AC-4/FR-5/NFR-6) — needs File & Doc Mgmt
  (S26) object storage not present; synchronous export already works (returns the file inline with a `Deferred`
  flag). NOTE: US-NTF-001 in-app notifications now exist, enabling a future "export ready" notification.
- PostgreSQL RLS (NFR-3), 10M-row perf (NFR-2), BRIN/GIN index tuning (NFR-7) — platform/DB infra (US-PLT-002).

### Tests (12 new in `AuditLogServiceTests`, full suite 2507 pass / 0 fail / 0 skip)
view-row-written-on-List, NOT-on-Get, one-row-per-request-no-recurse, multi-action IN, multi-resourceType IN,
single-value back-compat, single+multi fold into one OR group, action AND resourceType cross-group, keyword
matches before+after, actor autocomplete tenant-scoped+distinct, actor name/email filter, filter-options
tenant-scoped distinct. Reused the existing InMemory + seeded-timestamp pattern (no Testcontainers).
