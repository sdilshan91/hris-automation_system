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
