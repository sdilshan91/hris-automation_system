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
