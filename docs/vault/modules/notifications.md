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
